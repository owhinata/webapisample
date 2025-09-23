using System;
using MyNotificationHub;
using MyWebApi;

namespace MyAppMain;

/// <summary>
/// Bridges <see cref="MyWebApiHost"/> events to the <see cref="IAppController"/> abstraction.
/// </summary>
public sealed class WebApiController : IAppController, IAsyncDisposable
{
    private readonly MyWebApiHost _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebApiController"/> class.
    /// </summary>
    /// <param name="port">Port to start the Web API on.</param>
    /// <param name="id">Optional controller identifier.</param>
    public WebApiController(int port, string? id = null)
    {
        _host = new MyWebApiHost(port);
        Id = id ?? $"webapi:{port}";
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
    /// Allows callers (primarily tests) to configure the underlying host before start.
    /// </summary>
    /// <param name="configure">Callback invoked with the underlying host instance.</param>
    public void ConfigureHost(Action<MyWebApiHost> configure)
    {
        configure?.Invoke(_host);
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
