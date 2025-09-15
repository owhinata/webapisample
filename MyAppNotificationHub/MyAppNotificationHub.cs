namespace MyAppNotificationHub;

/// <summary>
/// MyAppNotificationHub acts as a synchronous notification hub between MyAppMain
/// and external observers (e.g., UI/View). MyAppMain calls HandleStart/End,
/// which synchronously invoke corresponding events for subscribers.
/// If asynchronous processing is needed, subscribers should offload work
/// on their side.
/// </summary>
public class MyAppNotificationHub
{
    // Result notifications (preferred for MVC)
    public event Action<ModelResult>? StartCompleted;
    public event Action<ModelResult>? EndCompleted;

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

    // Public helpers for model to raise results
    public void NotifyStartCompleted(ModelResult result) =>
        StartCompleted?.Invoke(result);

    public void NotifyEndCompleted(ModelResult result) =>
        EndCompleted?.Invoke(result);

    // IMU notifications
    public record ImuConnectionChangedDto(bool Connected, string? RemoteEndPoint);
    public record ImuStateChangedDto(bool IsOn);
    public record ImuVector3(float X, float Y, float Z);
    public record ImuSampleDto(ulong TimestampNs, ImuVector3 Gyro, ImuVector3 Accel);

    public event Action<ImuConnectionChangedDto>? ImuConnected;
    public event Action<ImuConnectionChangedDto>? ImuDisconnected;
    public event Action<ImuStateChangedDto>? ImuStateUpdated;
    public event Action<ImuSampleDto>? ImuSampleReceived;

    public void NotifyImuConnected(ImuConnectionChangedDto dto) => ImuConnected?.Invoke(dto);
    public void NotifyImuDisconnected(ImuConnectionChangedDto dto) => ImuDisconnected?.Invoke(dto);
    public void NotifyImuStateUpdated(ImuStateChangedDto dto) => ImuStateUpdated?.Invoke(dto);
    public void NotifyImuSample(ImuSampleDto dto) => ImuSampleReceived?.Invoke(dto);
}
