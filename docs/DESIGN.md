# Design: MyWebApi + MyAppMain + ExternalLib

## Goals
- Add two libraries around existing `MyWebApi` to execute application logic on POST requests.
- Avoid coupling `MyAppMain` to ASP.NET Core/DI; use events from `MyWebApi` instead.
- Keep external library unchanged (no new interfaces); integrate via delegates from `MyAppMain`.

## Components
- `MyWebApi` (existing)
  - Self‑hosts Kestrel, exposes versioned endpoints `/v1/start` and `/v1/end`.
  - Raises events with deserialized POST payloads.
- `MyAppMain` (new)
  - Orchestrator that wraps `MyWebApiHost` with `Start(int port)` / `Stop()`.
  - Subscribes to `MyWebApi` events and invokes application logic delegates wired to `ExternalLib`.
- `ExternalLib` (existing)
  - Contains real application logic methods (sync/async, static/instance). No changes required.

## Public Contracts
Defined in `MyWebApi` and used across the system.

```csharp
// Request DTOs (event payloads)
public sealed record StartCommand(string? Message);
public sealed record EndCommand(string? Message);

// Events exposed by MyWebApiHost (async delegates for flexibility)
public event Func<StartCommand, Task>? StartRequested;
public event Func<EndCommand, Task>? EndRequested;
```

Notes:
- Use `Func<T, Task>` for async compatibility. If handlers are sync, return `Task.CompletedTask`.
- We intentionally avoid `EventHandler<T>` because it requires `T : EventArgs` and adds no value here.

## Data Flow
1. Client sends `POST /v1/start` or `/v1/end` with JSON body `{ "message": "..." }`.
2. Minimal API model binding maps JSON to `StartCommand`/`EndCommand`.
3. `MyWebApiHost` raises `StartRequested`/`EndRequested` events with the DTOs.
4. `MyAppMain` subscribed handlers are invoked; they call into `ExternalLib` via delegates.

## MyAppMain Responsibilities
- Lifecycle:
  - `Start(int port)`: Subscribes to events, starts `MyWebApiHost` on the given port.
  - `Stop()`: Stops `MyWebApiHost` and unsubscribes handlers.
- Integration points:
  - Accepts two delegates in constructor:
    - `Func<StartCommand, Task> onStart`
    - `Func<EndCommand, Task> onEnd`
  - These delegates typically wrap existing `ExternalLib` methods.

Example wiring (no changes to ExternalLib required):

```csharp
// ExternalLib usage examples
var external = new ExternalLib.Service();
var app = new MyAppMain(
    onStart: cmd => external.HandleStartAsync(cmd.Message),
    onEnd:   cmd => external.HandleEndAsync(cmd.Message)
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
- Inject fake delegates that complete `TaskCompletionSource<T>` when invoked.
- Use `HttpClient` to POST to `/v1/start` and `/v1/end` and assert that fakes were called with expected payloads.
- Use a helper to allocate a free port by binding `TcpListener` to port 0.

Example test sketch:

```csharp
[TestMethod]
public async Task Posting_Start_Triggers_External_Handler()
{
    var startTcs = new TaskCompletionSource<StartCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
    var app = new MyAppMain(cmd => { startTcs.SetResult(cmd); return Task.CompletedTask; }, _ => Task.CompletedTask);
    var port = GetFreePort();
    try
    {
        app.Start(port);
        var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var res = await client.PostAsync("/v1/start", new StringContent("{\"message\":\"hello\"}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var received = await startTcs.Task.TimeoutAfter(TimeSpan.FromSeconds(3));
        Assert.AreEqual("hello", received.Message);
    }
    finally { app.Stop(); }
}
```

## Concurrency & Error Handling
- Multiple subscribers: If multiple handlers are attached, `await Task.WhenAll(invocations)` ensures all complete.
- Handler failures: Decide policy (fail fast vs. log and continue). Default recommendation: catch, log, and return 200 to the client unless failures must be surfaced.
- Timeouts: Consider wrapping delegate invocation with a timeout in `MyAppMain` if required by business needs.

## Security Considerations
- Sample endpoints are unauthenticated; add auth (API keys, JWT) if exposed beyond trusted networks.
- Prefer HTTPS in production; configure `app.Urls` accordingly.
- Validate/limit payload size to prevent abuse.

## Open Questions / Future Work
- Should start/end be idempotent or carry correlation IDs?
- Backpressure/queueing if handlers are long‑running.
- Structured logging and diagnostics events.

