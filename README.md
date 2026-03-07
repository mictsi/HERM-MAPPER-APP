# HERM-MAPPER-APP

> **⚠️ WARNING: This project is an experiment. Use at your own risk.**

## Overview
HERM-MAPPER-APP is a .NET web application for mapping product relationships and capabilities. It provides dashboards, import/export services, and reference catalogues for managing product mappings.

The web application now lives under `src/HERM-MAPPER-APP` and automated tests live under `tests/HERM-MAPPER-APP.Tests`.

## Features
- Product mapping and relationship management
- CSV export and workbook import services
- Dashboard and catalogue views
- Experimental status: subject to change

## Project Structure
- `src/HERM-MAPPER-APP/`: ASP.NET Core MVC application
- `tests/HERM-MAPPER-APP.Tests/`: unit tests
- `scripts/`: deployment and automation scripts
- `src/HERM-MAPPER-APP/Controllers/`: MVC controllers for app logic
- `src/HERM-MAPPER-APP/Data/`: Entity Framework database context
- `src/HERM-MAPPER-APP/Models/`: data models
- `src/HERM-MAPPER-APP/Services/`: business logic and import/export services
- `src/HERM-MAPPER-APP/ViewModels/`: view model classes
- `src/HERM-MAPPER-APP/Views/`: Razor views for UI
- `src/HERM-MAPPER-APP/wwwroot/`: static assets

## Getting Started
1. Clone the repository
2. Open in Visual Studio or VS Code
3. Restore NuGet packages
4. Build and run the project

## Installation Instructions

### Windows
1. Install [.NET SDK](https://dotnet.microsoft.com/download) (version 10.0 or later)
2. Open a terminal in the project directory
3. Run:
	```powershell
	dotnet restore .\HERM-MAPPER-APP.sln
	dotnet build .\HERM-MAPPER-APP.sln
	dotnet run --project .\src\HERM-MAPPER-APP\HERM-MAPPER-APP.csproj
	```
4. Access the app at the displayed local URL

### macOS
1. Install [.NET SDK](https://dotnet.microsoft.com/download) (version 10.0 or later)
2. Open Terminal and navigate to the project directory
3. Run:
	```bash
	dotnet restore ./HERM-MAPPER-APP.sln
	dotnet build ./HERM-MAPPER-APP.sln
	dotnet run --project ./src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj
	```
4. Access the app at the displayed local URL

### Linux
1. Install [.NET SDK](https://dotnet.microsoft.com/download) (version 10.0 or later)
2. Open Terminal and navigate to the project directory
3. Run:
	```bash
	dotnet restore ./HERM-MAPPER-APP.sln
	dotnet build ./HERM-MAPPER-APP.sln
	dotnet run --project ./src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj
	```
4. Access the app at the displayed local URL

> For all platforms, you can also use Visual Studio or VS Code for a graphical experience.

## Development
- Configuration files: `src/HERM-MAPPER-APP/appsettings.json`, `src/HERM-MAPPER-APP/appsettings.Development.json`
- Main entry point: `src/HERM-MAPPER-APP/Program.cs`
- Solution file: `HERM-MAPPER-APP.sln`

## Configuration
- Database provider is selected with `Database:Provider` or `HERM_Database__Provider`
- SQLite uses `Database:SqliteFilePath`, `ConnectionStrings:Sqlite`, or `ConnectionStrings:DefaultConnection`
- SQL Server uses `Database:ConnectionString`, `ConnectionStrings:SqlServer`, or `ConnectionStrings:DefaultConnection`
- SQLite paths support `|DataDirectory|` and `|HomeDirectory|` tokens; `|HomeDirectory|/data/...` is suitable for durable Azure App Service storage
- Console logging can be controlled with `Diagnostics:Console:*` or `HERM_Diagnostics__Console__*`
- SQL command logging can be controlled with `Diagnostics:Sql:*` or `HERM_Diagnostics__Sql__*`

Example environment variables:
```powershell
$env:HERM_Database__Provider = "SqlServer"
$env:HERM_Database__ConnectionString = "Server=localhost;Database=HermMapper;Trusted_Connection=True;TrustServerCertificate=True"
$env:HERM_Diagnostics__Sql__Enabled = "true"
$env:HERM_Diagnostics__Sql__LogLevel = "Information"
```

## Azure App Service Deploy
Two deployment scripts exist under `scripts/`:

- `deploy-appservice.ps1`: original script (keeps legacy behavior).
- `deploy-appservice-azcli.ps1`: recommended. Uses Azure CLI and is runnable from the repository root.

`deploy-appservice-azcli.ps1` behavior:
- Uses an existing App Service plan specified by `-AppPlan` (errors if not found).
- Creates the Web App if it does not exist, using the provided plan.
- Publishes the project under `src/HERM-MAPPER-APP`, zips the publish output and deploys via `az webapp deploy`.
- Loads the provided appsettings JSON file, flattens nested keys using `Section__Key` naming, and applies them as App Settings (in chunks).

Usage (run from the repository root):
```powershell
.\scripts\deploy-appservice-azcli.ps1 \
	-SubscriptionId $SUBID \
	-Region 'eastus' \
	-ResourceGroupName $RG \
	-WebAppName 'my-app' \
	-SettingsFile '.\src\HERM-MAPPER-APP\appsettings.Production.json' \
	-AppEnvironment 'Production' \
	-AppPlan $appplan
```

Notes:
- The resource group must already exist because the script derives the region from it.
- `deploy-appservice-azcli.ps1` requires the `az` CLI and `dotnet` SDK on PATH.
- Settings are flattened into App Service environment variables using `Section__Key` naming.

## License
See LICENSE for details.

---

> **Note:** This project is experimental and may not be production-ready. Contributions and feedback are welcome.
