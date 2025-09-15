using MyWebApi;
using MyAppNotificationHub;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;

namespace MyAppMain;

/// <summary>
/// Main application class that manages Web API host and TCP client connections.
/// Provides event-based integration with start/end operations and TCP server connectivity.
/// </summary>
public sealed class MyAppMain : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly MyWebApiHost _host = new();
    private readonly MyAppNotificationHub.MyAppNotificationHub? _notificationHub;
    private readonly List<IAppController> _controllers = new();
    private Channel<MyAppNotificationHub.ModelCommand>? _commandChannel;
    private Channel<MyAppNotificationHub.ModelResult>? _notifyChannel;
    private CancellationTokenSource? _cts;
    private Task? _processorTask;
    private Task? _notifierTask;
    private TcpClient? _tcpClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the MyAppMain class.
    /// </summary>
    /// <param name="notificationHub">Optional event hub. If null, events are not raised.</param>
    public MyAppMain(MyAppNotificationHub.MyAppNotificationHub? notificationHub = null)
    {
        _notificationHub = notificationHub;
        // Controllers are registered explicitly; default WebAPI controller is created on Start(port).
    }

    /// <summary>
    /// Gets a value indicating whether the Web API host is currently running.
    /// </summary>
    public bool IsRunning => _host.IsRunning;

    /// <summary>
    /// Gets a value indicating whether the TCP client is currently connected.
    /// </summary>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    /// <summary>
    /// Synchronously starts the Web API host on the specified port.
    /// </summary>
    /// <param name="port">The port number to listen on (1-65535).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if started successfully, false otherwise.</returns>
    public bool Start(int port, CancellationToken cancellationToken = default)
        => StartAsync(port, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Synchronously stops the Web API host.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if stopped successfully, false otherwise.</returns>
    public bool Stop(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Asynchronously starts the Web API host on the specified port.
    /// </summary>
    /// <param name="port">The port number to listen on (1-65535).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if started successfully, false otherwise.</returns>
    public async Task<bool> StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (!IsValidPort(port))
            return false;

        lock (_lock)
        {
            if (_disposed)
                return false;
        }

        if (_cts is not null) return false; // already started

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _commandChannel = Channel.CreateUnbounded<MyAppNotificationHub.ModelCommand>();
        _notifyChannel = Channel.CreateUnbounded<MyAppNotificationHub.ModelResult>();

        // Start background processor and notifier to separate contexts
        _processorTask = Task.Run(() => ProcessCommandsAsync(_cts.Token));
        _notifierTask = Task.Run(() => DispatchNotificationsAsync(_cts.Token));

        // If no controllers registered, add default WebAPI controller for the given port
        if (_controllers.Count == 0)
        {
            var adapter = new WebApiControllerAdapter(_host, port);
            RegisterController(adapter);
        }

        // Start all controllers
        foreach (var c in _controllers)
        {
            var ok = await c.StartAsync(_cts.Token);
            if (!ok) return false;
        }

        return true;
    }

    /// <summary>
    /// Asynchronously stops the Web API host.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if stopped successfully, false otherwise.</returns>
    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_disposed)
                return false;
        }

        var cts = _cts;
        _cts = null;
        try
        {
            if (cts is null) return false;
            cts.Cancel();

            // Stop all controllers
            foreach (var c in _controllers)
            {
                try { await c.StopAsync(cancellationToken); } catch { }
            }

            // Wait for workers
            if (_processorTask is not null)
                try { await _processorTask; } catch { }
            if (_notifierTask is not null)
                try { await _notifierTask; } catch { }

            return true;
        }
        finally
        {
            cts?.Dispose();
            _commandChannel = null;
            _notifyChannel = null;
        }
    }

    /// <summary>
    /// Event handler for start message received from Web API host.
    /// Processes the JSON message and establishes TCP connection if address and port are provided.
    /// </summary>
    /// <param name="json">The JSON message containing connection details.</param>
    public void OnStartMessageReceived(string json)
    {
        lock (_lock)
        {
            if (_disposed) return;
        }
        // Enqueue as command when using direct subscription (legacy path)
        _commandChannel?.Writer.TryWrite(new MyAppNotificationHub.ModelCommand("webapi:legacy", "start", json, null, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Event handler for end message received from Web API host.
    /// Processes the JSON message and disconnects from TCP server.
    /// </summary>
    /// <param name="json">The JSON message containing end details.</param>
    public void OnEndMessageReceived(string json)
    {
        lock (_lock)
        {
            if (_disposed) return;
        }
        _commandChannel?.Writer.TryWrite(new MyAppNotificationHub.ModelCommand("webapi:legacy", "end", json, null, DateTimeOffset.UtcNow));
    }

    public void RegisterController(IAppController controller)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        _controllers.Add(controller);
        controller.CommandRequested += cmd => _commandChannel?.Writer.TryWrite(cmd);
    }

    /// <summary>
    /// Disposes the application asynchronously.
    /// </summary>
    /// <returns>A ValueTask representing the disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        CleanupResources();
    }

    #region Private Methods

    /// <summary>
    /// Validates that the port number is within valid range
    /// </summary>
    /// <param name="port">The port number to validate.</param>
    /// <returns>True if the port is valid, false otherwise.</returns>
    private static bool IsValidPort(int port)
    {
        return port > 0 && port <= 65535;
    }

    /// <summary>
    /// Connects to a TCP server at the specified address and port.
    /// </summary>
    /// <param name="address">The server address to connect to.</param>
    /// <param name="port">The server port to connect to.</param>
    private void ConnectToTcpServer(string address, int port)
    {
        try
        {
            DisconnectFromTcpServer(); // Ensure any existing connection is closed

            _tcpClient = new TcpClient();
            _tcpClient.Connect(address, port);
            Console.WriteLine($"Connected to TCP server at {address}:{port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TCP connection failed: {ex.Message}");
            _tcpClient?.Dispose();
            _tcpClient = null;
        }
    }

    /// <summary>
    /// Disconnects from the current TCP server connection.
    /// </summary>
    private void DisconnectFromTcpServer()
    {
        if (_tcpClient != null)
        {
            try
            {
                _tcpClient.Close();
                Console.WriteLine("Disconnected from TCP server");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disconnecting: {ex.Message}");
            }
            finally
            {
                _tcpClient.Dispose();
                _tcpClient = null;
            }
        }
    }

    /// <summary>
    /// Cleans up resources and unsubscribes from events.
    /// </summary>
    private void CleanupResources()
    {
        if (_disposed) return;

        DisconnectFromTcpServer();
        _disposed = true;
    }

    private async Task ProcessCommandsAsync(CancellationToken ct)
    {
        var reader = _commandChannel!.Reader;
        while (!ct.IsCancellationRequested)
        {
            MyAppNotificationHub.ModelCommand cmd;
            try
            {
                if (!await reader.WaitToReadAsync(ct)) break;
                if (!reader.TryRead(out cmd)) continue;
            }
            catch (OperationCanceledException) { break; }

            var result = await HandleCommandAsync(cmd, ct);
            _notifyChannel?.Writer.TryWrite(result);
        }
    }

    private async Task<MyAppNotificationHub.ModelResult> HandleCommandAsync(MyAppNotificationHub.ModelCommand cmd, CancellationToken ct)
    {
        try
        {
            if (cmd.Type == "start")
            {
                var doc = JsonDocument.Parse(cmd.RawJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("address", out var addressProp) &&
                    root.TryGetProperty("port", out var portProp))
                {
                    var address = addressProp.GetString();
                    var port = portProp.GetInt32();
                    if (!string.IsNullOrEmpty(address) && IsValidPort(port))
                    {
                        ConnectToTcpServer(address!, port);
                    }
                }
                return new MyAppNotificationHub.ModelResult(cmd.ControllerId, cmd.Type, true, null, new { Connected = IsConnected }, cmd.CorrelationId, DateTimeOffset.UtcNow);
            }
            else if (cmd.Type == "end")
            {
                DisconnectFromTcpServer();
                return new MyAppNotificationHub.ModelResult(cmd.ControllerId, cmd.Type, true, null, new { Connected = IsConnected }, cmd.CorrelationId, DateTimeOffset.UtcNow);
            }
            else
            {
                return new MyAppNotificationHub.ModelResult(cmd.ControllerId, cmd.Type, false, "Unknown command type", null, cmd.CorrelationId, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex)
        {
            return new MyAppNotificationHub.ModelResult(cmd.ControllerId, cmd.Type, false, ex.Message, null, cmd.CorrelationId, DateTimeOffset.UtcNow);
        }
    }

    private async Task DispatchNotificationsAsync(CancellationToken ct)
    {
        var reader = _notifyChannel!.Reader;
        while (!ct.IsCancellationRequested)
        {
            MyAppNotificationHub.ModelResult res;
            try
            {
                if (!await reader.WaitToReadAsync(ct)) break;
                if (!reader.TryRead(out res)) continue;
            }
            catch (OperationCanceledException) { break; }

            if (res.Type == "start")
                _notificationHub?.NotifyStartCompleted(res);
            else if (res.Type == "end")
                _notificationHub?.NotifyEndCompleted(res);
        }
    }

    #endregion
}
