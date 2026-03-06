# HERM-MAPPER-APP

> **⚠️ WARNING: This project is an experiment. Use at your own risk.**

## Overview
HERM-MAPPER-APP is a .NET web application for mapping product relationships and capabilities. It provides dashboards, import/export services, and reference catalogues for managing product mappings.

## Features
- Product mapping and relationship management
- CSV export and workbook import services
- Dashboard and catalogue views
- Experimental status: subject to change

## Project Structure
- `Controllers/`: MVC controllers for app logic
- `Data/`: Entity Framework database context
- `Models/`: Data models
- `Services/`: Business logic and import/export services
- `ViewModels/`: View model classes
- `Views/`: Razor views for UI
- `wwwroot/`: Static assets (CSS, JS, libraries)

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
	dotnet restore
	dotnet build
	dotnet run
	```
4. Access the app at the displayed local URL

### macOS
1. Install [.NET SDK](https://dotnet.microsoft.com/download) (version 10.0 or later)
2. Open Terminal and navigate to the project directory
3. Run:
	```bash
	dotnet restore
	dotnet build
	dotnet run
	```
4. Access the app at the displayed local URL

### Linux
1. Install [.NET SDK](https://dotnet.microsoft.com/download) (version 10.0 or later)
2. Open Terminal and navigate to the project directory
3. Run:
	```bash
	dotnet restore
	dotnet build
	dotnet run
	```
4. Access the app at the displayed local URL

> For all platforms, you can also use Visual Studio or VS Code for a graphical experience.

## Development
- Configuration files: `appsettings.json`, `appsettings.Development.json`
- Main entry point: `Program.cs`
- Solution file: `HERM-MAPPER-APP.sln`

## License
See LICENSE for details.

---

> **Note:** This project is experimental and may not be production-ready. Contributions and feedback are welcome.
