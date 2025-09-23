namespace MyAppMain;

/// <summary>
/// Payload attached to ModelResult entries produced by IMU control commands.
/// </summary>
/// <param name="Status">Result status associated with the command.</param>
/// <param name="IsConnected">Indicates whether the IMU client reports an active connection.</param>
/// <param name="Message">Message that accompanies the result.</param>
internal sealed record ImuCommandPayload(
    ImuControlStatus Status,
    bool IsConnected,
    string Message
);
