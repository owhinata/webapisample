using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Linq;
using System.IO;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MyWebApi;

public sealed class MyWebApiHost : IAsyncDisposable
{
    private readonly object _lock = new();
    private WebApplication? _app;
    private CancellationTokenSource? _linkedCts;

    public bool IsRunning => _app is not null;

    // Events raised when /v1/start and /v1/end are posted (raw JSON/body string)
    // Keep Action<string> for handlers; we will invoke them concurrently and await completion.
    public event Action<string>? StartRequested;
    public event Action<string>? EndRequested;

    // Sync wrappers as requested (Start/Stop)
    public bool Start(int port, CancellationToken cancellationToken = default)
        => StartAsync(port, cancellationToken).GetAwaiter().GetResult();

    public bool Stop(CancellationToken cancellationToken = default)
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
            // Configure global concurrency limiter: 1 concurrent request, no queue.
            builder.Services.AddRateLimiter(o =>
            {
                o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 1,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });

            app = builder.Build();
            app.Urls.Add($"http://0.0.0.0:{port}");
            // Enable the rate limiter middleware
            app.UseRateLimiter();

            // POST-only sample endpoints under versioned route group /v1
            var v1 = app.MapGroup("/v1");
            v1.MapPost("/start", async (HttpRequest request) =>
            {
                var body = await new StreamReader(request.Body).ReadToEndAsync();
                if (StartRequested is not null)
                {
                    var handlers = StartRequested.GetInvocationList().Cast<Action<string>>();
                    var tasks = handlers.Select(h => Task.Run(() => h(body)));
                    await Task.WhenAll(tasks);
                }
                return Results.Created("/v1/start", new { message = "started" });
            });
            v1.MapPost("/end", async (HttpRequest request) =>
            {
                var body = await new StreamReader(request.Body).ReadToEndAsync();
                if (EndRequested is not null)
                {
                    var handlers = EndRequested.GetInvocationList().Cast<Action<string>>();
                    var tasks = handlers.Select(h => Task.Run(() => h(body)));
                    await Task.WhenAll(tasks);
                }
                return Results.Created("/v1/end", new { message = "ended" });
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

    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
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
            return false;

        var success = false;
        try
        {
            linkedCts?.Cancel();
            await app.StopAsync(cancellationToken);
            success = true;
        }
        finally
        {
            await app.DisposeAsync();
            linkedCts?.Dispose();
        }
        return success;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
