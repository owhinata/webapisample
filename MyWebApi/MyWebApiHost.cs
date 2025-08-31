using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace MyWebApi;

public sealed class MyWebApiHost : IAsyncDisposable
{
    private readonly object _lock = new();
    private WebApplication? _app;
    private Task? _runTask;
    private CancellationTokenSource? _linkedCts;

    public bool IsRunning => _app is not null;

    // Sync wrappers as requested (Start/Stop)
    public void Start(int port, CancellationToken cancellationToken = default)
        => StartAsync(port, cancellationToken).GetAwaiter().GetResult();

    public void Stop(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken).GetAwaiter().GetResult();

    // Async counterparts
    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_app is not null)
                throw new InvalidOperationException("Web API is already started.");
        }

        var options = new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        };

        var builder = WebApplication.CreateBuilder(options);

        var app = builder.Build();
        app.Urls.Add($"http://0.0.0.0:{port}");

        // POST-only sample endpoints under versioned route group /v1
        var v1 = app.MapGroup("/v1");
        v1.MapPost("/start", () => Results.Ok(new { message = "started" }));
        v1.MapPost("/end", () => Results.Ok(new { message = "ended" }));

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_lock)
        {
            _app = app;
            _linkedCts = linkedCts;
            _runTask = app.RunAsync(linkedCts.Token);
        }

        // Yield back to caller once hosting has begun
        await Task.Yield();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        WebApplication? app;
        Task? runTask;
        CancellationTokenSource? linkedCts;

        lock (_lock)
        {
            app = _app;
            runTask = _runTask;
            linkedCts = _linkedCts;
            _app = null;
            _runTask = null;
            _linkedCts = null;
        }

        if (app is null)
            return;

        try
        {
            linkedCts?.Cancel();
            await app.StopAsync(cancellationToken);
        }
        finally
        {
            await app.DisposeAsync();
            linkedCts?.Dispose();
            if (runTask is not null)
            {
                try { await runTask; } catch { /* ignore */ }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
