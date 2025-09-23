using System;
using System.Text.Json;
using System.Threading.Tasks;
using MyNotificationHub;

namespace MyAppMain;

/// <summary>
/// Translates high-level model commands into IMU operations and results.
/// </summary>
internal sealed class CommandHandler
{
    private readonly ImuClient _imuClient;
    private readonly object _ownershipSync = new();
    private string? _currentOwnerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandHandler"/> class.
    /// </summary>
    /// <param name="imuClient">IMU client used to manage connections.</param>
    public CommandHandler(ImuClient imuClient)
    {
        _imuClient = imuClient;
    }

    /// <summary>
    /// Executes the supplied <see cref="ModelCommand"/> and returns the resulting model state.
    /// </summary>
    /// <param name="command">Command emitted by a controller.</param>
    /// <returns>Completion result describing the outcome.</returns>
    public ValueTask<ModelResult> HandleAsync(ModelCommand command)
    {
        try
        {
            return command.Type switch
            {
                "start" => new ValueTask<ModelResult>(HandleStart(command)),
                "end" => new ValueTask<ModelResult>(HandleStop(command)),
                _ => new ValueTask<ModelResult>(
                    ErrorResult(
                        command.ControllerId,
                        command.Type,
                        "Unknown command type",
                        command.CorrelationId
                    )
                ),
            };
        }
        catch (Exception ex)
        {
            return new ValueTask<ModelResult>(
                ErrorResult(
                    command.ControllerId,
                    command.Type,
                    ex.Message,
                    command.CorrelationId
                )
            );
        }
    }

    /// <summary>
    /// Clears ownership state if held by the specified controller.
    /// </summary>
    /// <param name="controllerId">Identifier of the controller being removed.</param>
    public void ReleaseOwnership(string controllerId)
    {
        lock (_ownershipSync)
        {
            if (_currentOwnerId == controllerId)
                _currentOwnerId = null;
        }
    }

    /// <summary>
    /// Resets ownership, typically after a full application shutdown.
    /// </summary>
    public void ResetOwnership()
    {
        lock (_ownershipSync)
        {
            _currentOwnerId = null;
        }
    }

    private ModelResult HandleStart(ModelCommand command)
    {
        var (address, port) = TryParseServerInfo(command.RawJson);
        ImuControlResult controlResult;
        lock (_ownershipSync)
        {
            controlResult = EvaluateStart(command.ControllerId, address, port);
        }

        return ToModelResult(command, controlResult);
    }

    private ModelResult HandleStop(ModelCommand command)
    {
        ImuControlResult controlResult;
        lock (_ownershipSync)
        {
            controlResult = EvaluateStop(command.ControllerId);
        }

        return ToModelResult(command, controlResult);
    }

    private ImuControlResult EvaluateStart(
        string controllerId,
        string? address,
        int? port
    )
    {
        if (_currentOwnerId is null)
        {
            try
            {
                if (address is not null && port is not null)
                    _imuClient.Connect(address, port.Value);

                _currentOwnerId = controllerId;
                var message = address is not null
                    ? $"IMU start accepted for {controllerId} at {address}:{port}."
                    : $"IMU start accepted for {controllerId}.";
                return new ImuControlResult(ImuControlStatus.Success, message);
            }
            catch (Exception ex)
            {
                _currentOwnerId = null;
                return new ImuControlResult(
                    ImuControlStatus.Failed,
                    $"IMU start failed: {ex.Message}"
                );
            }
        }

        if (string.Equals(_currentOwnerId, controllerId, StringComparison.Ordinal))
        {
            return new ImuControlResult(
                ImuControlStatus.AlreadyRunning,
                "IMU already started by this controller."
            );
        }

        return new ImuControlResult(
            ImuControlStatus.OwnershipError,
            "IMU is currently controlled by another controller."
        );
    }

    private ImuControlResult EvaluateStop(string controllerId)
    {
        if (_currentOwnerId is null)
        {
            _imuClient.Disconnect();
            return new ImuControlResult(
                ImuControlStatus.Success,
                "IMU stop accepted; no owner was assigned."
            );
        }

        if (!string.Equals(_currentOwnerId, controllerId, StringComparison.Ordinal))
        {
            return new ImuControlResult(
                ImuControlStatus.OwnershipError,
                "Only the owning controller can stop the IMU."
            );
        }

        _currentOwnerId = null;
        _imuClient.Disconnect();
        return new ImuControlResult(ImuControlStatus.Success, "IMU stop accepted.");
    }

    private ModelResult ToModelResult(
        ModelCommand command,
        ImuControlResult controlResult
    )
    {
        var success =
            controlResult.Status
                is ImuControlStatus.Success
                    or ImuControlStatus.AlreadyRunning;
        var payload = new ImuCommandPayload(
            controlResult.Status,
            _imuClient.IsConnected,
            controlResult.Message
        );

        return new ModelResult(
            command.ControllerId,
            command.Type,
            success,
            success ? null : controlResult.Message,
            payload,
            command.CorrelationId,
            DateTimeOffset.UtcNow
        );
    }

    private (string? address, int? port) TryParseServerInfo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (
                root.TryGetProperty("address", out var addressProp)
                && root.TryGetProperty("port", out var portProp)
            )
            {
                var address = addressProp.GetString();
                var port = portProp.GetInt32();
                return (address, port);
            }
        }
        catch
        {
            // Ignore parse errors; treated as missing address/port.
        }

        return (null, null);
    }

    private static ModelResult ErrorResult(
        string controllerId,
        string type,
        string error,
        string? correlationId
    )
    {
        return new ModelResult(
            controllerId,
            type,
            false,
            error,
            null,
            correlationId,
            DateTimeOffset.UtcNow
        );
    }
}
