namespace IfUtilityLib;

/// <summary>
/// AppEventJunction acts as a synchronous event junction between MyAppMain
/// and external observers (e.g., UI/View). MyAppMain calls HandleStart/End,
/// which synchronously invoke corresponding events for subscribers.
/// If asynchronous processing is needed, subscribers should offload work
/// on their side.
/// </summary>
public class AppEventJunction
{
    /// <summary>
    /// Raised when MyAppMain reports a start event. Invoked synchronously.
    /// </summary>
    public event Action<string>? StartRequested;

    /// <summary>
    /// Raised when MyAppMain reports an end event. Invoked synchronously.
    /// </summary>
    public event Action<string>? EndRequested;

    /// <summary>
    /// Called by MyAppMain when a start request is received.
    /// Invokes StartRequested synchronously with the provided JSON payload.
    /// </summary>
    public virtual void HandleStart(string json)
    {
        StartRequested?.Invoke(json);
    }

    /// <summary>
    /// Called by MyAppMain when an end request is received.
    /// Invokes EndRequested synchronously with the provided JSON payload.
    /// </summary>
    public virtual void HandleEnd(string json)
    {
        EndRequested?.Invoke(json);
    }
}
