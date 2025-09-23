using System.Collections.Generic;
using System.Linq;
using MyNotificationHub;
using MyWebApi;
using NotificationHub = MyNotificationHub.MyNotificationHub;

namespace MyAppMain;

/// <summary>
/// Main application class that manages Web API host and TCP client
/// connections. Provides event-based integration with start/end operations
/// and TCP server connectivity.
/// </summary>
public sealed class MyAppMain : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly NotificationHub? _notificationHub;
    private readonly List<IAppController> _controllers = new();
    private readonly ImuClient _imuClient;
    private readonly CommandHandler _commandHandler;
    private readonly CommandPipeline _commandPipeline;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the MyAppMain class.
    /// </summary>
    /// <param name="notificationHub">
    /// Optional event hub. If null, events are not raised.
    /// </param>
    public MyAppMain(NotificationHub? notificationHub = null)
    {
        _notificationHub = notificationHub;
        _imuClient = new ImuClient(notificationHub);
        _commandHandler = new CommandHandler(_imuClient);
        _commandPipeline = new CommandPipeline(_commandHandler, notificationHub);
        // Controllers are registered explicitly; default WebAPI controller is
        // created on Start(port).
    }

    /// <summary>
    /// Gets a value indicating whether the application is currently running.
    /// </summary>
    public bool IsRunning => _cts is not null;

    /// <summary>
    /// Gets a value indicating whether the TCP client is currently connected.
    /// </summary>
    public bool IsConnected => _imuClient.IsConnected;

    /// <summary>
    /// Synchronously starts the Web API host on the specified port.
    /// </summary>
    /// <param name="port">The port number to listen on (1-65535).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if started successfully, false otherwise.</returns>
    public bool Start(CancellationToken cancellationToken = default) =>
        StartAsync(cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Synchronously stops the Web API host.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if stopped successfully, false otherwise.</returns>
    public bool Stop(CancellationToken cancellationToken = default) =>
        StopAsync(cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Asynchronously starts the Web API host on the specified port.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if started successfully, false otherwise.</returns>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_disposed)
                return false;
        }

        if (_cts is not null)
            return false; // already started

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        _cts = linkedCts;
        _commandPipeline.Start(linkedCts.Token);

        var startedControllers = new List<IAppController>();

        try
        {
            foreach (var controller in _controllers)
            {
                var ok = await controller.StartAsync(linkedCts.Token);
                if (!ok)
                {
                    await HandleStartFailureAsync(
                        linkedCts,
                        startedControllers,
                        cancellationToken
                    );
                    return false;
                }
                startedControllers.Add(controller);
            }

            return true;
        }
        catch
        {
            await HandleStartFailureAsync(
                linkedCts,
                startedControllers,
                cancellationToken
            );
            throw;
        }
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
            if (cts is null)
                return false;
            cts.Cancel();

            await _commandPipeline.StopAsync(cancellationToken);

            // Stop all controllers
            foreach (var c in _controllers)
            {
                try
                {
                    await c.StopAsync(cancellationToken);
                }
                catch { }
            }

            _imuClient.Disconnect();
            _commandHandler.ResetOwnership();
            return true;
        }
        finally
        {
            cts?.Dispose();
        }
    }

    public void RegisterController(IAppController controller)
    {
        if (controller == null)
            throw new ArgumentNullException(nameof(controller));

        if (_controllers.Contains(controller))
            return;

        _controllers.Add(controller);
        controller.CommandRequested -= HandleControllerCommandRequested;
        controller.CommandRequested += HandleControllerCommandRequested;
    }

    /// <summary>
    /// Unregisters a controller that was previously added.
    /// </summary>
    /// <param name="controller">Controller to unregister.</param>
    /// <returns>True if the controller was removed; otherwise false.</returns>
    public bool UnregisterController(IAppController controller)
    {
        if (controller == null)
            throw new ArgumentNullException(nameof(controller));

        var removed = _controllers.Remove(controller);
        if (!removed)
            return false;

        controller.CommandRequested -= HandleControllerCommandRequested;
        _commandHandler.ReleaseOwnership(controller.Id);
        return true;
    }

    /// <summary>
    /// Disposes the application asynchronously.
    /// </summary>
    /// <returns>A ValueTask representing the disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _commandPipeline.DisposeAsync();
        CleanupResources();
    }

    #region Private Methods

    /// <summary>
    /// Cleans up resources and unsubscribes from events.
    /// </summary>
    private void CleanupResources()
    {
        if (_disposed)
            return;

        _imuClient.Dispose();
        _commandHandler.ResetOwnership();
        _disposed = true;
    }

    /// <summary>
    /// Cancels startup and rolls back any controllers that have already started.
    /// </summary>
    /// <param name="linkedCts">Cancellation source governing the startup operations.</param>
    /// <param name="startedControllers">Controllers that were successfully started.</param>
    /// <param name="stopToken">Token used while stopping controllers and the pipeline.</param>
    private async Task HandleStartFailureAsync(
        CancellationTokenSource linkedCts,
        IReadOnlyCollection<IAppController> startedControllers,
        CancellationToken stopToken
    )
    {
        linkedCts.Cancel();

        foreach (var controller in startedControllers.Reverse())
        {
            try
            {
                await controller.StopAsync(stopToken);
            }
            catch { }
        }

        await _commandPipeline.StopAsync(stopToken);
        _imuClient.Disconnect();
        _commandHandler.ResetOwnership();
        linkedCts.Dispose();
        _cts = null;
    }

    private void HandleControllerCommandRequested(ModelCommand command)
    {
        _commandPipeline.TryWriteCommand(command);
    }

    #endregion
}
