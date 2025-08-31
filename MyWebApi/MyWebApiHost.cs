using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Linq;
using System.IO;

namespace MyWebApi;

public sealed class MyWebApiHost : IAsyncDisposable
{
    private readonly object _lock = new();
    private WebApplication? _app;
    private CancellationTokenSource? _linkedCts;

    public bool IsRunning => _app is not null;

    // Events raised when /v1/start and /v1/end are posted (raw JSON/body string)
    public event Func<string, Task>? StartRequested;
    public event Func<string, Task>? EndRequested;

    // Sync wrappers as requested (Start/Stop)
    public bool Start(int port, CancellationToken cancellationToken = default)
        => StartAsync(port, cancellationToken).GetAwaiter().GetResult();

    public void Stop(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken).GetAwaiter().GetResult();

    // Async counterparts
    public async Task<bool> StartAsync(int port, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_app is not null)
                return false;
        }

        var options = new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
        };

        WebApplication? app = null;
        CancellationTokenSource? linkedCts = null;
        try
        {
            var builder = WebApplication.CreateBuilder(options);
            app = builder.Build();
            app.Urls.Add($"http://0.0.0.0:{port}");

            // POST-only sample endpoints under versioned route group /v1
            var v1 = app.MapGroup("/v1");
            v1.MapPost("/start", async (HttpRequest request) =>
            {
                var body = await new StreamReader(request.Body).ReadToEndAsync();
                if (StartRequested is not null)
                {
                    var handlers = StartRequested.GetInvocationList().Cast<Func<string, Task>>();
                    await Task.WhenAll(handlers.Select(h => h(body)));
                }
                return Results.Ok(new { message = "started" });
            });
            v1.MapPost("/end", async (HttpRequest request) =>
            {
                var body = await new StreamReader(request.Body).ReadToEndAsync();
                if (EndRequested is not null)
                {
                    var handlers = EndRequested.GetInvocationList().Cast<Func<string, Task>>();
                    await Task.WhenAll(handlers.Select(h => h(body)));
                }
                return Results.Ok(new { message = "ended" });
            });

            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await app.StartAsync(linkedCts.Token);

            lock (_lock)
            {
                _app = app;
                _linkedCts = linkedCts;
            }

            return true;
        }
        catch
        {
            // Best-effort cleanup on failure
            if (app is not null)
            {
                try { await app.DisposeAsync(); } catch { }
            }
            linkedCts?.Dispose();
            return false;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        WebApplication? app;
        CancellationTokenSource? linkedCts;

        lock (_lock)
        {
            app = _app;
            linkedCts = _linkedCts;
            _app = null;
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
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
