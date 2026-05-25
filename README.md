# best-agent

BestAgent is a .NET 9 MVP scaffold for a single-agent runtime built with ASP.NET Core, MediatR, EF Core, and PostgreSQL.

## Projects

- `src/BestAgent.Api`: HTTP API and application bootstrap
- `src/BestAgent.Application`: MediatR handlers and runtime orchestration
- `src/BestAgent.Domain`: core entities and enums
- `src/BestAgent.Infrastructure`: EF Core persistence, model gateway, tools, seed data
- `src/BestAgent.Contracts`: request and response DTOs
- `tests/BestAgent.UnitTests`: unit tests
- `tests/BestAgent.IntegrationTests`: HTTP-level integration tests

## Run

1. Update `src/BestAgent.Api/appsettings.json` with your PostgreSQL and OpenAI-compatible gateway settings.
2. Run `dotnet tool restore`.
3. Run `dotnet build BestAgent.sln`.
4. Start the API with `dotnet run --project src/BestAgent.Api`.

The application applies EF Core migrations on startup and seeds the default `support-main` agent definition when the database is empty.

## Test

- Unit tests: `dotnet test tests/BestAgent.UnitTests/BestAgent.UnitTests.csproj`
- Integration tests: `dotnet test tests/BestAgent.IntegrationTests/BestAgent.IntegrationTests.csproj`
