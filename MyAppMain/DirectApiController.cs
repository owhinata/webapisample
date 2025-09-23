using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyNotificationHub;

namespace MyAppMain;

/// <summary>
/// Provides direct (in-process) start/stop access to the IMU through
/// <see cref="MyAppMain"/>.
/// </summary>
public sealed class DirectApiController : IAppController
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DirectApiController"/> class.
    /// </summary>
    /// <param name="id">Optional controller identifier.</param>
    public DirectApiController(string? id = null)
    {
        Id = id ?? $"direct:{Guid.NewGuid():N}";
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public event Action<ModelCommand>? CommandRequested;

    /// <summary>
    /// Starts the IMU using an address/port payload.
    /// </summary>
    public Task<bool> StartImuAsync(
        string address,
        int port,
        CancellationToken ct = default
    )
    {
        var payload = JsonSerializer.Serialize(new { address, port });
        return StartImuAsync(payload, ct);
    }

    /// <summary>
    /// Starts the IMU using an address/port payload synchronously.
    /// </summary>
    public bool StartImu(string address, int port)
    {
        var payload = JsonSerializer.Serialize(new { address, port });
        return StartImu(payload);
    }

    /// <summary>
    /// Starts the IMU with a caller-provided payload.
    /// </summary>
    public Task<bool> StartImuAsync(
        string payloadJson,
        CancellationToken ct = default
    )
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<bool>(ct);

        var handler = CommandRequested;
        if (handler is null)
            return Task.FromResult(false);

        handler(CreateCommand("start", payloadJson));
        return Task.FromResult(true);
    }

    /// <summary>
    /// Starts the IMU synchronously with a caller-provided payload.
    /// </summary>
    public bool StartImu(string payloadJson)
    {
        return StartImuAsync(payloadJson).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Stops the IMU.
    /// </summary>
    public Task<bool> StopImuAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<bool>(ct);

        var handler = CommandRequested;
        if (handler is null)
            return Task.FromResult(false);

        handler(CreateCommand("end", "{}"));
        return Task.FromResult(true);
    }

    /// <summary>
    /// Stops the IMU synchronously.
    /// </summary>
    public bool StopImu()
    {
        return StopImuAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public Task<bool> StartAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> StopAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    private ModelCommand CreateCommand(string type, string payloadJson)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        return new ModelCommand(
            Id,
            type,
            payloadJson,
            correlationId,
            DateTimeOffset.UtcNow
        );
    }
}
