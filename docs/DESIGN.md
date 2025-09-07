# Design: MyWebApi + MyAppMain + ExternalLib

## Goals
- Add two libraries around existing `MyWebApi` to execute application logic on POST requests.
- Avoid coupling `MyAppMain` to ASP.NET Core/DI; use events from `MyWebApi` instead.
- Keep external library unchanged (no new interfaces); integrate via delegates from `MyAppMain`.

## Components
- `MyWebApi` (existing)
  - Self‑hosts Kestrel, exposes versioned endpoints `/v1/start` and `/v1/end`.
  - Raises events with raw POST body strings.
  - Implements global rate limiting (1 concurrent request, no queueing).
  - Returns 201 Created on success, 429 Too Many Requests when rate limited.
- `MyAppMain` (new)
  - Orchestrator that wraps `MyWebApiHost` with `Start(int port)` / `Stop()`.
  - Subscribes to `MyWebApi` events and invokes application logic delegates wired to `ExternalLib`.
- `ExternalLib` (existing)
  - Contains real application logic methods (sync/async, static/instance). No changes required.

## Public Contracts
To avoid coupling to MyWebApi types, events and delegates use raw JSON strings.

```csharp
// Events exposed by MyWebApiHost (synchronous for simplicity)
public event Action<string>? StartRequested; // raw body
public event Action<string>? EndRequested;   // raw body
```

Notes:
- Events use `Action<string>` for synchronous handling.
- For async operations in handlers, consider using Task.Run or async void patterns if needed.

## Data Flow
1. Client sends `POST /v1/start` or `/v1/end` with JSON body `{ "message": "..." }`.
2. Minimal API reads the body as a string.
3. `MyWebApiHost` raises `StartRequested`/`EndRequested` with the raw JSON string.
4. `MyAppMain` subscribed handlers are invoked; they call into `ExternalLib` via delegates.

## MyAppMain Responsibilities
- Lifecycle:
  - `Start(int port)`: Subscribes to events, starts `MyWebApiHost` on the given port.
  - `Stop()`: Stops `MyWebApiHost` and unsubscribes handlers.
- Integration points:
  - Accepts two delegates in constructor:
    - `Action<string> onStart`
    - `Action<string> onEnd`
  - These delegates typically wrap existing `ExternalLib` methods.

Example wiring (no changes to ExternalLib required):

```csharp
// ExternalLib usage examples
var external = new ExternalLib.Service();
var app = new MyAppMain(
    onStart: json => external.HandleStart(json),
    onEnd:   json => external.HandleEnd(json)
);

app.Start(5008);
// ...
app.Stop();
```

## Dependency Boundaries
- `MyAppMain` does not reference ASP.NET Core abstractions (no `IServiceCollection`, `WebApplication`, etc.).
- Coordination happens via events and plain DTOs only.
- `ExternalLib` remains unchanged; it is called through delegates passed into `MyAppMain`.

## Versioning
- Endpoints are grouped under `/v1` to enable future breaking changes under `/v2` while keeping `/v1` stable.

## Testing Strategy (Black‑Box)
- Test project `MyAppMain.Tests` uses MSTest.
- Start `MyAppMain` on a free port.
- Inject fake delegates that complete `TaskCompletionSource<string>` when invoked.
- Use `HttpClient` to POST to `/v1/start` and `/v1/end` and assert that fakes were called and contain expected JSON.
- Use a helper to allocate a free port by binding `TcpListener` to port 0.

Example test sketch:

```csharp
[TestMethod]
public async Task Posting_Start_Triggers_External_Handler()
{
    var startTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var app = new MyAppMain(json => startTcs.SetResult(json), _ => { });
    var port = GetFreePort();
    try
    {
        app.Start(port);
        var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var res = await client.PostAsync("/v1/start", new StringContent("{\"message\":\"hello\"}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        var received = await startTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(3));
        StringAssert.Contains(received, "\"hello\"");
    }
    finally { app.Stop(); }
}
```

## Concurrency & Error Handling
- Rate limiting: Global rate limiter allows only 1 concurrent request (no queueing). Returns 429 Too Many Requests when limit exceeded.
- Multiple subscribers: If multiple handlers are attached, all are invoked synchronously in order.
- Handler failures: Decide policy (fail fast vs. log and continue). Default recommendation: catch, log, and return 201 Created to the client unless failures must be surfaced.
- Timeouts: Consider implementing timeout logic within handlers if required by business needs.

## Security Considerations
- Sample endpoints are unauthenticated; add auth (API keys, JWT) if exposed beyond trusted networks.
- Prefer HTTPS in production; configure `app.Urls` accordingly.
- Validate/limit payload size to prevent abuse.

## Open Questions / Future Work
- Should start/end be idempotent or carry correlation IDs?
- Backpressure/queueing if handlers are long‑running.
- Structured logging and diagnostics events.
