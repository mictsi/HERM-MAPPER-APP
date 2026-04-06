# HERM-MAPPER-APP

> **⚠️ WARNING: Use this project at your own risk.**

## Overview

HERM-MAPPER-APP is a .NET web application for mapping product relationships and capabilities. It includes reference catalogues, import/export workflows, dashboards, and AI-assisted mapping features.

The web application lives under `src/HERM-MAPPER-APP` and automated tests live under `tests/HERM-MAPPER-APP.Tests`.

## Contents

- [Features](#features)
- [Project Structure](#project-structure)
- [Quick Start](#quick-start)
- [Documentation](#documentation)
- [License](#license)

## Features

- Product mapping and relationship management
- CSV export and workbook import services
- Dashboard and catalogue views
- AI-assisted mapping with configurable providers
- Experimental status: subject to change

## Project Structure

- `src/HERM-MAPPER-APP/`: ASP.NET Core MVC application
- `tests/HERM-MAPPER-APP.Tests/`: unit tests
- `scripts/`: deployment and automation scripts
- `docs/`: project documentation

Key application folders:

- `src/HERM-MAPPER-APP/Controllers/`
- `src/HERM-MAPPER-APP/Data/`
- `src/HERM-MAPPER-APP/Models/`
- `src/HERM-MAPPER-APP/Services/`
- `src/HERM-MAPPER-APP/ViewModels/`
- `src/HERM-MAPPER-APP/Views/`
- `src/HERM-MAPPER-APP/wwwroot/`

## Quick Start

1. Clone the repository.
2. Install the .NET SDK 10.0 or later.
3. Restore and build the solution.
4. Run the web app.

Windows:

```powershell
dotnet restore .\HERM-MAPPER-APP.sln
dotnet build .\HERM-MAPPER-APP.sln
dotnet run --project .\src\HERM-MAPPER-APP\HERM-MAPPER-APP.csproj
```

macOS / Linux:

```bash
dotnet restore ./HERM-MAPPER-APP.sln
dotnet build ./HERM-MAPPER-APP.sln
dotnet run --project ./src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj
```

## Documentation

### Installation

- [Installation Guide](docs/installation.md)

### Development

- [Development Guide](docs/development.md)

### Configuration

- [Configuration Guide](docs/configuration.md)
- [Microsoft Entra App Registration Setup](docs/entra-app-registration-setup.md)

### Deployment

- [Azure App Service Deployment](docs/deployment-appservice.md)

### AI

- [AI Overview](docs/ai.md)
- [Azure AI Foundry Agent Setup](docs/azure-ai-foundry-agent.md)
- TRM markdown reference files:
  - [01-TRM-Domain.md](docs/trm_model/3.2/01-TRM-Domain.md)
  - [02-TRM-Capability.md](docs/trm_model/3.2/02-TRM-Capability.md)
  - [03-TRM-Component.md](docs/trm_model/3.2/03-TRM-Component.md)
  - [HERM-TRM-V320-explainer.md](docs/trm_model/3.2/HERM-TRM-V320-explainer.md)
  - [TRM-LLM-Instructions-v2.md](docs/trm_model/3.2/TRM-LLM-Instructions-v2.md)

## License

See [LICENSE](LICENSE) for details.

---

> **Note:** This project is experimental and may not be production-ready. Contributions and feedback are welcome.
