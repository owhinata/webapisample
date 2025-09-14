using MyWebApi;
using IfUtilityLib;
using System.Net.Sockets;
using System.Text.Json;

namespace MyAppMain;

public sealed class MyAppMain : IDisposable
{
    private readonly MyWebApiHost _host = new();
    private readonly IfUtility _utility;
    private TcpClient? _tcpClient;
    private bool _disposed;

    public MyAppMain(IfUtility? utility = null)
    {
        _utility = utility ?? new IfUtility();
        // Subscribe at construction time to avoid missing early POSTs right after Start.
        _host.StartRequested += OnStartMessageReceived;
        _host.EndRequested += OnEndMessageReceived;
    }

    public bool IsRunning => _host.IsRunning;
    public bool IsConnected => _tcpClient?.Connected ?? false;

    public void Start(int port) => _host.Start(port);

    public void Stop() => _host.Stop();

    // Event handlers wired to MyWebApiHost events
    public void OnStartMessageReceived(string json)
    {
        // Call IfUtility first
        _utility.HandleStart(json);

        // Then handle TCP connection directly
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("address", out var addressProp) &&
                root.TryGetProperty("port", out var portProp))
            {
                var address = addressProp.GetString();
                var port = portProp.GetInt32();

                ConnectToTcpServer(address!, port);
            }
        }
        catch (Exception ex)
        {
            // Log or handle connection error
            Console.WriteLine($"Failed to connect to TCP server: {ex.Message}");
        }
    }

    public void OnEndMessageReceived(string json)
    {
        // Call IfUtility first
        _utility.HandleEnd(json);

        // Then handle TCP disconnection
        DisconnectFromTcpServer();
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _host.StartRequested -= OnStartMessageReceived;
        _host.EndRequested -= OnEndMessageReceived;
        DisconnectFromTcpServer();
        _disposed = true;
    }
}
