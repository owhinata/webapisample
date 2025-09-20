namespace MyAppMain;

/// <summary>
/// Defines the contract for application controllers that surface commands to the model.
/// </summary>
public interface IAppController
{
    /// <summary>
    /// Gets the controller identifier.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Raised when a controller emits a command for the model to handle.
    /// </summary>
    event Action<MyAppNotificationHub.ModelCommand>? CommandRequested;

    /// <summary>
    /// Starts the controller.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>True if startup succeeded.</returns>
    Task<bool> StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the controller.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>True if shutdown succeeded.</returns>
    Task<bool> StopAsync(CancellationToken ct = default);
}
