# MyWebApi & MyAppMain — Self-Hosted Web API with Orchestration

A .NET 8 solution providing a self-hosted Web API with rate limiting and event-based integration. The solution consists of three main components: MyWebApi (Web API host), MyAppMain (orchestrator), and IfUtility (shared utilities).

**Key Features**
- Self-hosted Web API with global rate limiting (1 concurrent request, no queueing)
- Event-driven architecture with synchronous event handlers
- Versioned endpoints: `/v1/start`, `/v1/end` (POST only)
- Returns 201 Created on success, 429 Too Many Requests when rate limited
- Simple lifecycle management: `Start(port)` and `Stop()`
- Delegate-based integration for decoupled architecture
- Targets `net8.0`

**Project Layout**
- `MyWebApi/` — Self-hosted Web API with rate limiting
  - `MyWebApiHost.cs` — Main host class with event-based integration
  - `MyWebApi.csproj` — Project file
- `MyAppMain/` — Orchestrator library
  - `MyAppMain.cs` — Main orchestration logic
  - `MyAppMain.csproj` — Project file
- `IfUtility/` — Shared utility library
  - `IfUtility.cs` — Utility functions
  - `IfUtility.csproj` — Project file
- `MyAppMain.Tests/` — MSTest black-box tests
  - `MyAppMainBlackBoxTests.cs` — Integration tests

**Requirements**
- .NET 8 SDK installed (`dotnet --version` shows 8.x)
- ASP.NET Core shared framework available (`dotnet --list-runtimes` shows `Microsoft.AspNetCore.App 8.x`)

If the target machine lacks ASP.NET Core runtime, publish your app self‑contained.

**Build & Run**

Build the solution:
```bash
# Build all projects
dotnet build -c Release

# Build specific project
dotnet build MyWebApi/MyWebApi.csproj -c Release
dotnet build MyAppMain/MyAppMain.csproj -c Release
dotnet build IfUtility/IfUtility.csproj -c Release
```

Run individual projects:
```bash
# Run MyAppMain
dotnet run --project MyAppMain

# Run with hot reload
dotnet watch run --project MyAppMain
```

**Quick Start**
Wire your existing application logic by passing delegates to MyAppMain. The delegates receive raw JSON strings from the HTTP POST body.

```csharp
using MyAppMain;

// Example: integrate existing external logic
var app = new MyAppMain.MyAppMain(
    onStart: json => {
        // ExternalLib.Start(json); // raw JSON string
        Console.WriteLine($"Start JSON: {json}");
    },
    onEnd: json => {
        // ExternalLib.End(json);
        Console.WriteLine($"End JSON: {json}");
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
- POST `/v1/start`: Returns 201 Created with `{ message: "started" }` on success, invokes `onStart` delegate with raw JSON
- POST `/v1/end`: Returns 201 Created with `{ message: "ended" }` on success, invokes `onEnd` delegate with raw JSON
- Both endpoints return 429 Too Many Requests when rate limit is exceeded (1 concurrent request allowed)

The endpoints are implemented in MyWebApiHost and raise synchronous events (`Action<string>`) that MyAppMain subscribes to.

**Behavior & Notes**
- `Start` throws if already started. Call `Stop` before starting again.
- The host binds `http://0.0.0.0:{port}` and is HTTP by default (enable HTTPS if needed).
- Delegates are synchronous (`Action<string>`). For async operations, use Task.Run within handlers.
- Rate limiting: Only 1 concurrent request is processed; additional requests receive 429 status.

**Testing (MSTest, black-box)**
- Run all tests: `dotnet test -c Release`
- Run specific project: `dotnet test MyAppMain.Tests -c Release`
- List tests: `dotnet test MyAppMain.Tests --list-tests`
- Filter tests: `dotnet test MyAppMain.Tests --filter "FullyQualifiedName~MyAppMainBlackBoxTests"`
- Tests start MyWebApiHost on a free port, POST payloads, and assert delegate invocation and rate limiting behavior.

**Documentation**
- `docs/DESIGN.md` — Architecture, contracts, and testing strategy
- `CLAUDE.md` — Repository guidelines, coding standards, and development workflow

**Troubleshooting**
- The framework 'Microsoft.AspNetCore.App' was not found: Install ASP.NET Core runtime or publish self-contained
- Port already in use: Choose another port in `Start(port)`
- 429 Too Many Requests: Rate limit exceeded, wait for the current request to complete

**Security**
- Samples are unauthenticated and HTTP only
- For production: Enable HTTPS, add authentication/authorization, validate payloads
- Configure CORS explicitly per environment
- Never commit secrets; use `dotnet user-secrets` for development

**License**
- Add your preferred license here if distributing.
