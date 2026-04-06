# Installation Guide

## Requirements

- .NET SDK 10.0 or later
- Git

Optional tools:

- Visual Studio
- Visual Studio Code
- Azure CLI for deployment workflows

## Clone the Repository

```bash
git clone <repository-url>
cd HERM-MAPPER-APP
```

## Build and Run

### Windows

```powershell
dotnet restore .\HERM-MAPPER-APP.sln
dotnet build .\HERM-MAPPER-APP.sln
dotnet run --project .\src\HERM-MAPPER-APP\HERM-MAPPER-APP.csproj
```

### macOS

```bash
dotnet restore ./HERM-MAPPER-APP.sln
dotnet build ./HERM-MAPPER-APP.sln
dotnet run --project ./src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj
```

### Linux

```bash
dotnet restore ./HERM-MAPPER-APP.sln
dotnet build ./HERM-MAPPER-APP.sln
dotnet run --project ./src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj
```

## First Launch

After the app starts, open the local URL shown in the terminal. The application will initialize its database and supporting tables on first run.

## Related Docs

- [Development Guide](development.md)
- [Configuration Guide](configuration.md)
