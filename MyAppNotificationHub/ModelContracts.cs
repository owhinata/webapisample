namespace MyAppNotificationHub;

public record ModelCommand(
    string ControllerId,
    string Type,
    string RawJson,
    string? CorrelationId,
    DateTimeOffset Timestamp);

public record ModelResult(
    string ControllerId,
    string Type,
    bool Success,
    string? Error,
    object? Payload,
    string? CorrelationId,
    DateTimeOffset CompletedAt);
