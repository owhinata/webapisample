# MyAppMain — Orchestrator Library for Self‑Hosted Web API

MyAppMain is a .NET 8 orchestrator library that starts/stops an embedded Web API and forwards POST payloads to your application logic via delegates. Consumers should use MyAppMain directly; the underlying MyWebApi host is an internal detail.

**Key Features**
- Simple lifecycle: `Start(port)` and `Stop()`; no ASP.NET Core dependency in your code.
- Delegate-based integration: wire existing ExternalLib methods without adding interfaces.
- Versioned endpoints exposed by the host: `/v1/start`, `/v1/end` (POST only).
- Targets `net8.0`.

**Project Layout**
- `MyAppMain/` — Orchestrator library (public entry)
  - `MyAppMain.csproj`, `MyAppMain.cs`
- `MyWebApi/` — Embedded host (internal dependency)
  - `MyWebApiHost.cs`, `Commands.cs` (DTOs `StartCommand`, `EndCommand`)
- `MyAppMain.Tests/` — MSTest black‑box tests for MyAppMain

**Requirements**
- .NET 8 SDK installed (`dotnet --version` shows 8.x)
- ASP.NET Core shared framework available (`dotnet --list-runtimes` shows `Microsoft.AspNetCore.App 8.x`)

If the target machine lacks ASP.NET Core runtime, publish your app self‑contained.

**Build**
- Build all: `dotnet build -c Release`
- Or build only MyAppMain: `dotnet build MyAppMain/MyAppMain.csproj -c Release`

**Quick Start**
Wire your existing application logic by passing delegates to MyAppMain. The delegates receive DTOs deserialized from the HTTP POST body.

```
using MyAppMain;

// Example: integrate existing external logic (sync or async)
var app = new MyAppMain.MyAppMain(
    onStart: json => {
        // ExternalLib.Start(json); // raw JSON string
        Console.WriteLine($"Start JSON: {json}");
        return Task.CompletedTask;
    },
    onEnd: json => {
        // ExternalLib.End(json);
        Console.WriteLine($"End JSON: {json}");
        return Task.CompletedTask;
    }
);

app.Start(5008); // listens on http://0.0.0.0:5008
// ... run your process ...
app.Stop();
```

Test the endpoints while running:
- `curl -X POST http://localhost:5008/v1/start -H "Content-Type: application/json" -d '{"message":"hello"}'`
- `curl -X POST http://localhost:5008/v1/end   -H "Content-Type: application/json" -d '{"message":"bye"}'`

**Endpoints**
- POST `/v1/start`: `{ message: "started" }` on success, also invokes your `onStart` delegate with `StartCommand`.
- POST `/v1/end`: `{ message: "ended" }` on success, also invokes your `onEnd` delegate with `EndCommand`.

Both endpoints are implemented in the internal host and raise events that MyAppMain subscribes to; your delegates are awaited.

**Behavior & Notes**
- `Start` throws if already started. Call `Stop` before starting again.
- The host binds `http://0.0.0.0:{port}` and is HTTP by default (enable HTTPS if needed).
- Delegates may be async; return `Task.CompletedTask` for sync logic.

**Testing (MSTest, black‑box)**
- Run tests: `dotnet test MyAppMain.Tests -c Release`
- Tests start MyAppMain on a free port, POST payloads, and assert delegate invocation.

**Design Document**
- See `docs/DESIGN.md` for architecture, contracts, and testing strategy.

**Troubleshooting**
- The framework 'Microsoft.AspNetCore.App' was not found: install ASP.NET Core runtime or publish self‑contained.
- Port already in use: choose another port in `Start(port)`.

**Security**
- Samples are unauthenticated and HTTP only; for production, enable HTTPS and add authN/Z.

**License**
- Add your preferred license here if distributing.
