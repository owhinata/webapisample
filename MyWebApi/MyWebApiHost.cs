using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Linq;
using System.IO;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MyWebApi;

/// <summary>
/// A self-hosted Web API host that provides versioned endpoints for start and end operations.
/// Supports rate limiting and event-based integration with external handlers.
/// </summary>
public sealed class MyWebApiHost : IAsyncDisposable
{
    private readonly object _lock = new();
    private WebApplication? _app;
    private CancellationTokenSource? _linkedCts;

    /// <summary>
    /// Gets a value indicating whether the Web API host is currently running.
    /// </summary>
    public bool IsRunning => _app is not null;

    /// <summary>
    /// Event raised when a POST request is made to /v1/start endpoint.
    /// The event handler receives the raw JSON body as a string.
    /// </summary>
    public event Action<string>? StartRequested;

    /// <summary>
    /// Event raised when a POST request is made to /v1/end endpoint.
    /// The event handler receives the raw JSON body as a string.
    /// </summary>
    public event Action<string>? EndRequested;

    /// <summary>
    /// Synchronously starts the Web API host on the specified port.
    /// </summary>
    /// <param name="port">The port number to listen on (1-65535).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if started successfully, false otherwise.</returns>
    public bool Start(int port, CancellationToken cancellationToken = default)
        => StartAsync(port, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Synchronously stops the Web API host.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if stopped successfully, false otherwise.</returns>
    public bool Stop(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Asynchronously starts the Web API host on the specified port.
    /// </summary>
    /// <param name="port">The port number to listen on (1-65535).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if started successfully, false otherwise.</returns>
    public async Task<bool> StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (!IsValidPort(port))
            return false;

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
            v1.MapPost("/start", HandleStartRequest);
            v1.MapPost("/end", HandleEndRequest);

            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await app.StartAsync(linkedCts.Token);

            lock (_lock)
            {
                _app = app;
                _linkedCts = linkedCts;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Best-effort cleanup on failure
            await CleanupResourcesAsync(app, linkedCts);
            return false;
        }
    }

    /// <summary>
    /// Asynchronously stops the Web API host.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if stopped successfully, false otherwise.</returns>
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

        try
        {
            linkedCts?.Cancel();
            await app.StopAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            return false;
        }
        finally
        {
            try
            {
                await app.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors
            }
            linkedCts?.Dispose();
        }
    }

    /// <summary>
    /// Disposes the Web API host asynchronously.
    /// </summary>
    /// <returns>A ValueTask representing the disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    #region Private Methods

    /// <summary>
    /// Validates that the port number is within valid range
    /// </summary>
    private static bool IsValidPort(int port)
    {
        return port > 0 && port <= 65535;
    }

    /// <summary>
    /// Handles the /v1/start endpoint request
    /// </summary>
    private async Task<IResult> HandleStartRequest(HttpRequest request)
    {
        var body = await ReadRequestBodyAsync(request);
        await InvokeEventHandlersAsync(StartRequested, body);
        return Results.Created("/v1/start", new { message = "started" });
    }

    /// <summary>
    /// Handles the /v1/end endpoint request
    /// </summary>
    private async Task<IResult> HandleEndRequest(HttpRequest request)
    {
        var body = await ReadRequestBodyAsync(request);
        await InvokeEventHandlersAsync(EndRequested, body);
        return Results.Created("/v1/end", new { message = "ended" });
    }

    /// <summary>
    /// Reads the request body as a string
    /// </summary>
    private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Invokes all registered event handlers concurrently
    /// </summary>
    private async Task InvokeEventHandlersAsync(Action<string>? eventHandler, string body)
    {
        if (eventHandler is null) return;

        var handlers = eventHandler.GetInvocationList().Cast<Action<string>>();
        var tasks = handlers.Select(handler => Task.Run(() => handler(body)));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Cleans up resources in case of failure
    /// </summary>
    private static async Task CleanupResourcesAsync(WebApplication? app, CancellationTokenSource? linkedCts)
    {
        if (app is not null)
        {
            try 
            { 
                await app.DisposeAsync(); 
            } 
            catch 
            { 
                // Ignore disposal errors during cleanup
            }
        }
        
        linkedCts?.Dispose();
    }

    #endregion
}
