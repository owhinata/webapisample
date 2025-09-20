namespace MyAppNotificationHub;

/// <summary>
/// Represents a command emitted by a controller for the model to process.
/// </summary>
public record ModelCommand(
    string ControllerId,
    string Type,
    string RawJson,
    string? CorrelationId,
    DateTimeOffset Timestamp
);

/// <summary>
/// Represents the outcome of processing a <see cref="ModelCommand"/>.
/// </summary>
public record ModelResult(
    string ControllerId,
    string Type,
    bool Success,
    string? Error,
    object? Payload,
    string? CorrelationId,
    DateTimeOffset CompletedAt
);
