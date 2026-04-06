# Development Guide

## Solution Layout

- App project: `src/HERM-MAPPER-APP`
- Test project: `tests/HERM-MAPPER-APP.Tests`
- Solution file: `HERM-MAPPER-APP.sln`

## Useful Files

- Main entry point: `src/HERM-MAPPER-APP/Program.cs`
- App settings: `src/HERM-MAPPER-APP/appsettings.json`
- Development settings: `src/HERM-MAPPER-APP/appsettings.Development.json`

## Build

```powershell
dotnet build .\HERM-MAPPER-APP.sln
```

## Run Tests

```powershell
dotnet test .\tests\HERM-MAPPER-APP.Tests\HERM-MAPPER-APP.Tests.csproj
```

## Production Build Artifacts

Runtime-specific build output can be produced with:

```powershell
.\build.ps1 -Target Prod -Runtime All
```

This generates artifacts under `artifacts/prod` for:

- `linux-x64`
- `linux-arm64`
- `win-x64`
- `win-arm64`

## Related Docs

- [Installation Guide](installation.md)
- [Configuration Guide](configuration.md)
- [Azure App Service Deployment](deployment-appservice.md)
