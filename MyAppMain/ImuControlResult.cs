namespace MyAppMain;

/// <summary>
/// Indicates the outcome of an IMU control operation.
/// </summary>
public enum ImuControlStatus
{
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// Operation succeeded because the IMU was already in the requested state for the caller.
    /// </summary>
    AlreadyRunning,

    /// <summary>
    /// Operation failed because another controller currently owns the IMU.
    /// </summary>
    OwnershipError,

    /// <summary>
    /// Operation failed for another reason.
    /// </summary>
    Failed,
}

/// <summary>
/// Result returned from IMU control convenience APIs.
/// </summary>
/// <param name="Status">High-level outcome indicator.</param>
/// <param name="Message">Human-readable explanation of the result.</param>
public sealed record ImuControlResult(ImuControlStatus Status, string Message);
