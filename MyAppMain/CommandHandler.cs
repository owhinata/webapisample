using System;
using System.Text.Json;
using System.Threading.Tasks;
using MyAppNotificationHub;

namespace MyAppMain;

internal sealed class CommandHandler
{
    private readonly ImuClient _imuClient;

    public CommandHandler(ImuClient imuClient)
    {
        _imuClient = imuClient;
    }

    public ValueTask<ModelResult> HandleAsync(ModelCommand command)
    {
        try
        {
            switch (command.Type)
            {
                case "start":
                    {
                        var (address, port) = TryParseServerInfo(command.RawJson);
                        if (address is not null && port is not null)
                            _imuClient.Connect(address, port.Value);

                        return new ValueTask<ModelResult>(
                            SuccessResult(
                                command.ControllerId,
                                command.Type,
                                command.CorrelationId
                            )
                        );
                    }
                case "end":
                    {
                        _imuClient.Disconnect();
                        return new ValueTask<ModelResult>(
                            SuccessResult(
                                command.ControllerId,
                                command.Type,
                                command.CorrelationId
                            )
                        );
                    }
                default:
                    return new ValueTask<ModelResult>(
                        ErrorResult(
                            command.ControllerId,
                            command.Type,
                            "Unknown command type",
                            command.CorrelationId
                        )
                    );
            }
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

    private ModelResult SuccessResult(
        string controllerId,
        string type,
        string? correlationId
    )
    {
        return new ModelResult(
            controllerId,
            type,
            true,
            null,
            new { Connected = _imuClient.IsConnected },
            correlationId,
            DateTimeOffset.UtcNow
        );
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
