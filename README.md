# MyWebApi — Self‑Hosted Minimal Web API Library

A minimal .NET 8 class library that self‑hosts an ASP.NET Core Web API. It exposes POST‑only sample endpoints and provides simple `Start`/`Stop` control from your code. TCP server functionality is independent and not required by this library.

**Key Features**
- Self‑hosts Kestrel within your process.
- Simple lifecycle: `Start(port)` and `Stop()`.
- POST endpoints: `/start`, `/end` returning JSON.
- Targets `net8.0` and references `Microsoft.AspNetCore.App`.

**Project Layout**
- `MyWebApi/` — Library project
  - `MyWebApi.csproj` — `net8.0` with `Microsoft.AspNetCore.App` framework reference
  - `MyWebApiHost.cs` — Host with `Start`/`Stop` and POST endpoints

**Requirements**
- .NET 8 SDK installed (`dotnet --version` shows 8.x)
- ASP.NET Core shared framework available (`dotnet --list-runtimes` shows `Microsoft.AspNetCore.App 8.x`)

If running on a machine without the ASP.NET Core runtime, either install it (e.g., `aspnetcore-runtime-8.0`) or publish self‑contained from your app.

**Build**
- Build the library:
  - `dotnet build MyWebApi/MyWebApi.csproj -c Release`
- Artifacts:
  - `MyWebApi/bin/Release/net8.0/MyWebApi.dll`

**Quick Start (From Your App)**
Use the host class to start and stop the embedded HTTP server.

```
using MyWebApi;

var host = new MyWebApiHost();

// Start on port 5008 (binds http://0.0.0.0:5008)
host.Start(5008);

// ... your app logic ...

// Stop when finished
host.Stop();
// Or: await host.DisposeAsync();
```

Test the endpoints while running:
- `curl -X POST http://localhost:5008/start` → `{ "message": "started" }`
- `curl -X POST http://localhost:5008/end` → `{ "message": "ended" }`

**Endpoints**
- POST `/start`: Returns `200 OK` with `{ message: "started" }`.
- POST `/end`: Returns `200 OK` with `{ message: "ended" }`.

Both endpoints are mapped via minimal APIs inside `MyWebApiHost` and are intentionally stateless for the sample. Extend the handlers as needed.

**Behavior & Notes**
- The host adds `http://0.0.0.0:{port}` to `app.Urls` so it listens on all interfaces.
- `Start` throws if already started; call `Stop`/`DisposeAsync` before starting again.
- `Stop` cancels the internal run loop and disposes the host.
- `MyWebApiHost` implements `IAsyncDisposable` for graceful shutdown.

**Troubleshooting**
- Error: `The framework 'Microsoft.AspNetCore.App' was not found`:
  - Install ASP.NET Core runtime or publish self‑contained.
- Error: Port already in use:
  - Choose another port in `Start(port)`.
- Build errors about missing `WebApplication`/`Results`:
  - Ensure `MyWebApi.csproj` has `<FrameworkReference Include="Microsoft.AspNetCore.App" />` and target `net8.0`.

**Extending**
- Add more POST endpoints in `MyWebApiHost` using `app.MapPost("/path", handler)`.
- Inject services by registering them on `builder.Services` before `builder.Build()` if you expand the design.

**License**
- Add your preferred license here if distributing.
