using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyNotificationHub;

namespace MyAppMain;

/// <summary>
/// Provides programmatic start/stop access to the IMU through <see cref="MyAppMain"/>.
/// </summary>
public sealed class ProgrammaticImuController : IAppController, ICommandPipelineAware
{
    private CommandPipeline? _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgrammaticImuController"/> class.
    /// </summary>
    /// <param name="id">Optional controller identifier.</param>
    public ProgrammaticImuController(string? id = null)
    {
        Id = id ?? $"programmatic:{Guid.NewGuid():N}";
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    [SuppressMessage(
        "CodeQuality",
        "CS0067",
        Justification = "Interface contract requires the event even though commands execute via pipeline access."
    )]
    public event Action<ModelCommand>? CommandRequested;

    /// <summary>
    /// Starts the IMU using an address/port payload.
    /// </summary>
    public Task<ImuControlResult> StartImuAsync(
        string address,
        int port,
        CancellationToken ct = default
    )
    {
        var payload = JsonSerializer.Serialize(new { address, port });
        return StartImuAsync(payload, ct);
    }

    /// <summary>
    /// Starts the IMU with a caller-provided payload.
    /// </summary>
    public async Task<ImuControlResult> StartImuAsync(
        string payloadJson,
        CancellationToken ct = default
    )
    {
        var command = CreateCommand("start", payloadJson);
        var result = await ExecuteAsync(command, ct).ConfigureAwait(false);
        return ToControlResult(result);
    }

    /// <summary>
    /// Stops the IMU.
    /// </summary>
    public async Task<ImuControlResult> StopImuAsync(CancellationToken ct = default)
    {
        var command = CreateCommand("end", "{}");
        var result = await ExecuteAsync(command, ct).ConfigureAwait(false);
        return ToControlResult(result);
    }

    /// <inheritdoc />
    public Task<bool> StartAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> StopAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    /// <inheritdoc />
    void ICommandPipelineAware.AttachPipeline(CommandPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    void ICommandPipelineAware.DetachPipeline(CommandPipeline pipeline)
    {
        if (ReferenceEquals(_pipeline, pipeline))
            _pipeline = null;
    }

    private static ImuControlResult ToControlResult(ModelResult result)
    {
        if (result.Payload is ImuCommandPayload payload)
            return new ImuControlResult(payload.Status, payload.Message);

        var status = result.Success
            ? ImuControlStatus.Success
            : ImuControlStatus.Failed;
        var message =
            result.Error
            ?? (result.Success ? "Operation completed." : "Operation failed.");
        return new ImuControlResult(status, message);
    }

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

    private Task<ModelResult> ExecuteAsync(ModelCommand command, CancellationToken ct)
    {
        var pipeline =
            _pipeline
            ?? throw new InvalidOperationException(
                "Controller is not registered with a running MyAppMain instance."
            );

        return pipeline.ExecuteCommandAsync(command, ct);
    }
}
