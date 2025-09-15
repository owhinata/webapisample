# Repository Guidelines

## Project Structure & Module Organization
- Root: Contains three main projects:
  - `MyWebApi/`: Self-hosted Web API with rate limiting (`MyWebApiHost.cs`)
  - `MyAppMain/`: Main application entry point
  - `AppEventJunction/`: Event junction library for external notifications
- MyWebApi features:
  - Event-based integration via `StartRequested`/`EndRequested` events
  - Versioned endpoints under `/v1` route group (`/v1/start`, `/v1/end`)
  - Global rate limiting (1 concurrent request, no queueing)
  - Returns 200 OK on success, 429 Too Many Requests when rate limited
- Add controllers (if used) under `Controllers/`; shared services under `Services/`; data/EF code under `Data/`.

## Build, Test, and Development Commands
- Restore/build: `dotnet restore` / `dotnet build` — restore packages and compile.
- Run individual projects: `dotnet run --project MyWebApi` or `dotnet run --project MyAppMain`.
- Hot reload: `dotnet watch run` — rebuilds on file changes.
- Unit tests: `dotnet test` — runs all test projects in the solution.

## Documentation
- High-level architecture and integration notes live in `docs/DESIGN.md`.

## Coding Style & Naming Conventions
- C#: 4-space indent, file-scoped namespaces, `nullable` enabled, implicit usings on (see `.csproj`).
- Names: PascalCase for types/methods, camelCase for locals/parameters, interfaces prefixed `I`.
- Files: one top-level type per file; group code by feature (e.g., `Weather/WeatherEndpoints.cs`).
- APIs: prefer minimal APIs/route groups; keep routes kebab-case (e.g., `/weather-forecasts`).

## Testing Guidelines
- Framework: MSTest v2 in `MyAppMain.Tests` (net8.0).
- Style: Black-box tests that start `MyWebApiHost` on a free port and call endpoints with `HttpClient`.
- Test projects: `MyAppMain.Tests` for testing the main application and API integration.
- Create project: `dotnet new mstest -n MyAppMain.Tests` then add references as needed.
- Run tests: `dotnet test MyAppMain.Tests -c Release` (or `--no-build` after a successful build).
- List/filter tests: `dotnet test MyAppMain.Tests --list-tests` / `dotnet test MyAppMain.Tests --filter "FullyQualifiedName~MyAppMainBlackBoxTests"`.
- Ports: Allocate a free port per test (e.g., bind `TcpListener` to port 0) to avoid conflicts in parallel runs.
- Optional in-proc testing: If needed, use `Microsoft.AspNetCore.TestHost` and refactor host wiring to allow in-memory testing without opening sockets.

## Commit & Pull Request Guidelines
- Code formatting: run `dotnet format` on all projects before committing to ensure consistent code style.
- Commits: use Conventional Commits (e.g., `feat: add weather endpoint`, `fix: handle null summary`).
- Subject line: single line, max 80 characters.
- Feature/Fix commits: add a brief description body (what/why, and how to validate) below the title.
- Chore commits: title only is sufficient; description body is optional.
- Do not include escape sequences like \n in commit messages. For multi-line bodies, use multiple `-m` flags: `git commit -m "subject" -m "body line 1" -m "body line 2"`.
- Wrap body lines at 80 characters (hard wrap paragraphs and bullet lines).
- PRs: include purpose/summary, linked issue, how to validate (URL or curl), and screenshots of Swagger
  when UI changes.
- Checks: PRs should build cleanly and keep public API changes documented in Swagger.

## Security & Configuration Tips
- Local HTTPS: if browsers warn, trust the dev cert: `dotnet dev-certs https --trust` (Ubuntu may need `libnss3-tools`).
- Config: prefer `appsettings.Development.json` and environment variables; never commit secrets — use `dotnet user-secrets` in Development.
- CORS/HTTPS: keep `app.UseHttpsRedirection()` in place; configure CORS explicitly per environment.
