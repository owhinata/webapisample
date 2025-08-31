# Repository Guidelines

## Project Structure & Module Organization
- Root: `Program.cs` (minimal API endpoints), `MyWebApi.csproj`, `appsettings*.json`, `Properties/launc
hSettings.json`, `MyWebApi.http`.
- Add controllers (if used) under `Controllers/`; shared services under `Services/`; data/EF code under
 `Data/`.
- Swagger/OpenAPI is enabled in Development; browse `https://localhost:7061/swagger` (ports from `launc
hSettings.json`).

## Build, Test, and Development Commands
- Restore/build: `dotnet restore` / `dotnet build` — restore packages and compile.
- Run (HTTPS): `dotnet run --launch-profile https` — starts the API with Swagger.
- Hot reload: `dotnet watch run` — rebuilds on file changes.
- Unit tests: `dotnet test` — runs tests when a test project exists.

## Documentation
- High-level architecture and integration notes live in `docs/DESIGN.md`.

## Coding Style & Naming Conventions
- C#: 4-space indent, file-scoped namespaces, `nullable` enabled, implicit usings on (see `.csproj`).
- Names: PascalCase for types/methods, camelCase for locals/parameters, interfaces prefixed `I`.
- Files: one top-level type per file; group code by feature (e.g., `Weather/WeatherEndpoints.cs`).
- APIs: prefer minimal APIs/route groups; keep routes kebab-case (e.g., `/weather-forecasts`).

## Testing Guidelines
- Framework: MSTest v2 in `MyWebApi.Tests` (net8.0).
- Style: Black-box tests that start `MyWebApiHost` on a free port and call endpoints with `HttpClient`.
- Create project: `dotnet new mstest -n MyWebApi.Tests` then `dotnet add MyWebApi.Tests/MyWebApi.Tests.csproj reference MyWebApi/MyWebApi.csproj`.
- Run tests: `dotnet test MyWebApi.Tests -c Release` (or `--no-build` after a successful build).
- List/filter tests: `dotnet test MyWebApi.Tests --list-tests` / `dotnet test MyWebApi.Tests --filter "FullyQualifiedName~MyWebApiHostBlackBoxTests"`.
- Ports: Allocate a free port per test (e.g., bind `TcpListener` to port 0) to avoid conflicts in parallel runs.
- Optional in-proc testing: If needed, use `Microsoft.AspNetCore.TestHost` and refactor host wiring to allow in-memory testing without opening sockets.

## Commit & Pull Request Guidelines
- Commits: use Conventional Commits (e.g., `feat: add weather endpoint`, `fix: handle null summary`).
- Subject line: single line, max 80 characters.
- Feature/Fix commits: add a brief description body (what/why, and how to validate) below the title.
- Chore commits: title only is sufficient; description body is optional.
- PRs: include purpose/summary, linked issue, how to validate (URL or curl), and screenshots of Swagger
  when UI changes.
- Checks: PRs should build cleanly and keep public API changes documented in Swagger.

## Security & Configuration Tips
- Local HTTPS: if browsers warn, trust the dev cert: `dotnet dev-certs https --trust` (Ubuntu may need 
`libnss3-tools`).
- Config: prefer `appsettings.Development.json` and environment variables; never commit secrets — use `
dotnet user-secrets` in Development.
- CORS/HTTPS: keep `app.UseHttpsRedirection()` in place; configure CORS explicitly per environment.
