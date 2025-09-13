[33mcommit fc579c3775c5ab392cb93124da88f75d5acd073a[m[33m ([m[1;36mHEAD[m[33m -> [m[1;32mmain[m[33m)[m
Author: KatsumiOuwa <d2p.yggdrasill@gmail.com>
Date:   Sat Sep 13 13:31:48 2025 +0900

    feat: add .cursor/rules for project guidelines
    
    Add comprehensive Cursor rules file covering:
    - Project architecture and event-driven design principles
    - C# coding standards and naming conventions
    - Testing guidelines with MSTest and black-box approach
    - Build, development, and security best practices
    - File organization and documentation standards

[1mdiff --git a/.cursor/rules b/.cursor/rules[m
[1mnew file mode 100644[m
[1mindex 0000000..4b45147[m
[1m--- /dev/null[m
[1m+++ b/.cursor/rules[m
[36m@@ -0,0 +1,99 @@[m
[32m+[m[32m# Cursor Rules for MyWebApi + MyAppMain + IfUtility[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Project Overview[m[41m[m
[32m+[m[32mThis is a .NET 8 solution with three main components:[m[41m[m
[32m+[m[32m- **MyWebApi**: Self-hosted Web API with rate limiting and event-based integration[m[41m[m
[32m+[m[32m- **MyAppMain**: Orchestrator that wraps MyWebApiHost with lifecycle management[m[41m[m
[32m+[m[32m- **IfUtility**: Shared utility library[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Architecture Principles[m[41m[m
[32m+[m[32m- Event-driven architecture with synchronous event handlers[m[41m[m
[32m+[m[32m- Decoupled design: MyAppMain uses delegates to integrate with external libraries[m[41m[m
[32m+[m[32m- No direct coupling to ASP.NET Core/DI in MyAppMain[m[41m[m
[32m+[m[32m- Versioned endpoints under `/v1` route group[m[41m[m
[32m+[m[32m- Global rate limiting (1 concurrent request, no queueing)[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Code Style & Standards[m[41m[m
[32m+[m[41m[m
[32m+[m[32m### C# Conventions[m[41m[m
[32m+[m[32m- Use 4-space indentation[m[41m[m
[32m+[m[32m- File-scoped namespaces[m[41m[m
[32m+[m[32m- Enable nullable reference types (`<Nullable>enable</Nullable>`)[m[41m[m
[32m+[m[32m- Enable implicit usings (`<ImplicitUsings>enable</ImplicitUsings>`)[m[41m[m
[32m+[m[32m- PascalCase for types, methods, properties, events[m[41m[m
[32m+[m[32m- camelCase for local variables and parameters[m[41m[m
[32m+[m[32m- Interfaces prefixed with `I`[m[41m[m
[32m+[m[32m- One top-level type per file[m[41m[m
[32m+[m[41m[m
[32m+[m[32m### Project Structure[m[41m[m
[32m+[m[32m- Controllers go in `Controllers/` directory[m[41m[m
[32m+[m[32m- Shared services in `Services/` directory[m[41m[m
[32m+[m[32m- Data/EF code in `Data/` directory[m[41m[m
[32m+[m[32m- Group code by feature (e.g., `Weather/WeatherEndpoints.cs`)[m[41m[m
[32m+[m[41m[m
[32m+[m[32m### API Design[m[41m[m
[32m+[m[32m- Prefer minimal APIs and route groups[m[41m[m
[32m+[m[32m- Use kebab-case for routes (e.g., `/weather-forecasts`)[m[41m[m
[32m+[m[32m- Version endpoints under `/v1`, `/v2`, etc.[m[41m[m
[32m+[m[32m- Return 201 Created on success, 429 Too Many Requests when rate limited[m[41m[m
[32m+[m[32m- Use raw JSON strings for event payloads to avoid coupling[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Event-Driven Integration[m[41m[m
[32m+[m[32m- MyWebApiHost exposes synchronous events: `StartRequested` and `EndRequested`[m[41m[m
[32m+[m[32m- Events use `Action<string>` for raw JSON body handling[m[41m[m
[32m+[m[32m- MyAppMain subscribes to events and invokes external library delegates[m[41m[m
[32m+[m[32m- For async operations in handlers, use `Task.Run` or async void patterns[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Testing Guidelines[m[41m[m
[32m+[m[32m- Use MSTest v2 framework[m[41m[m
[32m+[m[32m- Write black-box tests that start MyWebApiHost on free ports[m[41m[m
[32m+[m[32m- Use `HttpClient` to test endpoints[m[41m[m
[32m+[m[32m- Allocate free ports per test to avoid conflicts[m[41m[m
[32m+[m[32m- Test both success and rate limiting scenarios[m[41m[m
[32m+[m[32m- Use `TaskCompletionSource<string>` for testing event handlers[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Build & Development[m[41m[m
[32m+[m[32m- Target .NET 8 (`net8.0`)[m[41m[m
[32m+[m[32m- Use `dotnet build -c Release` for production builds[m[41m[m
[32m+[m[32m- Use `dotnet watch run` for development with hot reload[m[41m[m
[32m+[m[32m- Run tests with `dotnet test -c Release`[m[41m[m
[32m+[m[32m- Use `dotnet user-secrets` for development configuration[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Security & Configuration[m[41m[m
[32m+[m[32m- Never commit secrets; use `dotnet user-secrets` for development[m[41m[m
[32m+[m[32m- Configure CORS explicitly per environment[m[41m[m
[32m+[m[32m- Prefer HTTPS in production[m[41m[m
[32m+[m[32m- Validate and limit payload sizes[m[41m[m
[32m+[m[32m- Add authentication/authorization for production use[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Error Handling[m[41m[m
[32m+[m[32m- Catch, log, and continue for handler failures (unless failures must be surfaced)[m[41m[m
[32m+[m[32m- Implement timeout logic within handlers if required[m[41m[m
[32m+[m[32m- Handle rate limiting gracefully with 429 responses[m[41m[m
[32m+[m[32m- Use structured logging for diagnostics[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Commit & PR Guidelines[m[41m[m
[32m+[m[32m- Use Conventional Commits format[m[41m[m
[32m+[m[32m- Subject line: max 80 characters[m[41m[m
[32m+[m[32m- Include brief description body for features/fixes[m[41m[m
[32m+[m[32m- PRs should build cleanly and document public API changes[m[41m[m
[32m+[m[32m- Include validation steps and screenshots for UI changes[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## File Organization[m[41m[m
[32m+[m[32m- Keep related files together by feature[m[41m[m
[32m+[m[32m- Use descriptive file names that match their primary type[m[41m[m
[32m+[m[32m- Place shared utilities in IfUtility project[m[41m[m
[32m+[m[32m- Keep MyAppMain focused on orchestration logic[m[41m[m
[32m+[m[32m- Maintain clear separation between Web API and business logic[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Performance Considerations[m[41m[m
[32m+[m[32m- Rate limiting prevents system overload[m[41m[m
[32m+[m[32m- Synchronous event handling for simplicity[m[41m[m
[32m+[m[32m- Consider backpressure/queueing for long-running handlers[m[41m[m
[32m+[m[32m- Use appropriate HTTP status codes for different scenarios[m[41m[m
[32m+[m[41m[m
[32m+[m[32m## Documentation[m[41m[m
[32m+[m[32m- Keep architecture notes in `docs/DESIGN.md`[m[41m[m
[32m+[m[32m- Update README.md with usage examples[m[41m[m
[32m+[m[32m- Document public APIs and integration points[m[41m[m
[32m+[m[32m- Include troubleshooting guides for common issues[m[41m[m
