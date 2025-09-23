using MyNotificationHub;
using MyWebApi;

namespace MyAppMain;

/// <summary>
/// Bridges <see cref="MyWebApiHost"/> events to the <see cref="IAppController"/> abstraction.
/// </summary>
/// <summary>
/// Bridges <see cref="MyWebApiHost"/> events to the <see cref="IAppController"/> abstraction.
/// </summary>
public sealed class WebApiControllerAdapter : IAppController, IAsyncDisposable
{
    private readonly MyWebApiHost _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebApiControllerAdapter"/> class.
    /// </summary>
    /// <param name="host">Underlying Web API host.</param>
    /// <param name="id">Optional controller identifier.</param>
    public WebApiControllerAdapter(MyWebApiHost host, string? id = null)
    {
        _host = host;
        Id = id ?? $"webapi:{host.Port}";
        _host.StartRequested += body =>
            CommandRequested?.Invoke(
                new ModelCommand(Id, "start", body, null, DateTimeOffset.UtcNow)
            );
        _host.EndRequested += body =>
            CommandRequested?.Invoke(
                new ModelCommand(Id, "end", body, null, DateTimeOffset.UtcNow)
            );
    }

    /// <summary>
    /// Gets the controller identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Raised when a command is produced by the underlying host.
    /// </summary>
    public event Action<ModelCommand>? CommandRequested;

    /// <inheritdoc />
    public Task<bool> StartAsync(CancellationToken ct = default) =>
        _host.StartAsync(ct);

    /// <inheritdoc />
    public Task<bool> StopAsync(CancellationToken ct = default) =>
        _host.StopAsync(ct);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
    }
}
