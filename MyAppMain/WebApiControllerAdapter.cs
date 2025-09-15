using MyWebApi;

namespace MyAppMain;

public sealed class WebApiControllerAdapter : IAppController, IAsyncDisposable
{
    private readonly MyWebApiHost _host;
    private readonly int _port;

    public WebApiControllerAdapter(MyWebApiHost host, int port, string? id = null)
    {
        _host = host;
        _port = port;
        Id = id ?? $"webapi:{port}";
        _host.StartRequested += body => CommandRequested?.Invoke(new MyAppNotificationHub.ModelCommand(Id, "start", body, null, DateTimeOffset.UtcNow));
        _host.EndRequested += body => CommandRequested?.Invoke(new MyAppNotificationHub.ModelCommand(Id, "end", body, null, DateTimeOffset.UtcNow));
    }

    public string Id { get; }
    public event Action<MyAppNotificationHub.ModelCommand>? CommandRequested;

    public Task<bool> StartAsync(CancellationToken ct = default)
        => _host.StartAsync(_port, ct);

    public Task<bool> StopAsync(CancellationToken ct = default)
        => _host.StopAsync(ct);

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
    }
}
