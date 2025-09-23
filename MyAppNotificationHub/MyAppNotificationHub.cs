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
    // IMU notifications
    /// <summary>
    /// Describes an IMU connection state change.
    /// </summary>
    public record ImuConnectionChangedDto(bool Connected, string? RemoteEndPoint);

    /// <summary>
    /// Describes an IMU on/off transition.
    /// </summary>
    public record ImuStateChangedDto(bool IsOn);

    /// <summary>
    /// Represents a three dimensional vector value.
    /// </summary>
    public record ImuVector3(float X, float Y, float Z);

    /// <summary>
    /// Carries a single IMU sample.
    /// </summary>
    public record ImuSampleDto(ulong TimestampNs, ImuVector3 Gyro, ImuVector3 Accel);

    /// <summary>
    /// Raised when the IMU connection is established.
    /// </summary>
    public event Action<ImuConnectionChangedDto>? ImuConnected;

    /// <summary>
    /// Raised when the IMU connection is dropped.
    /// </summary>
    public event Action<ImuConnectionChangedDto>? ImuDisconnected;

    /// <summary>
    /// Raised when the IMU on/off state changes.
    /// </summary>
    public event Action<ImuStateChangedDto>? ImuStateUpdated;

    /// <summary>
    /// Raised when a new IMU sample is available.
    /// </summary>
    public event Action<ImuSampleDto>? ImuSampleReceived;

    /// <summary>
    /// Notifies subscribers that the IMU connection has been established.
    /// </summary>
    public void NotifyImuConnected(ImuConnectionChangedDto dto) =>
        ImuConnected?.Invoke(dto);

    /// <summary>
    /// Notifies subscribers that the IMU connection has been closed.
    /// </summary>
    public void NotifyImuDisconnected(ImuConnectionChangedDto dto) =>
        ImuDisconnected?.Invoke(dto);

    /// <summary>
    /// Notifies subscribers of an IMU state change.
    /// </summary>
    public void NotifyImuStateUpdated(ImuStateChangedDto dto) =>
        ImuStateUpdated?.Invoke(dto);

    /// <summary>
    /// Notifies subscribers of a new IMU sample.
    /// </summary>
    public void NotifyImuSample(ImuSampleDto dto) => ImuSampleReceived?.Invoke(dto);

    /// <summary>
    /// Dispatches a <see cref="ModelResult"/> to the appropriate completion event.
    /// </summary>
    /// <param name="result">The result produced by the command pipeline.</param>
    public void NotifyResult(ModelResult result)
    {
        ResultPublished?.Invoke(result);
    }

    /// <summary>
    /// Raised when any command finishes processing and a <see cref="ModelResult"/> is available.
    /// </summary>
    public event Action<ModelResult>? ResultPublished;
}
