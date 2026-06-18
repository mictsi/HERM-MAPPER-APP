# HERM-MAPPER-APP

## Overview

HERM-MAPPER-APP is a .NET web application for mapping product relationships and capabilities. It includes reference catalogues, import/export workflows, dashboards, and AI-assisted mapping features.

The web application lives under `src/HERM-MAPPER-APP` and automated tests live under `tests/HERM-MAPPER-APP.Tests`.

## Contents

- [Features](#features)
- [Project Structure](#project-structure)
- [Quick Start](#quick-start)
- [Documentation](#documentation)
- [Attribution](#attribution)
- [License](#license)

## Features

- Product mapping and relationship management
- CSV export and workbook import services for TRM, ARM, BRM, and DRM catalogues
- Dashboard and catalogue views, including the HERM browser hierarchy for DRM Topic Type -> Topic -> Entity -> Common Sub-Class
- Custom BRM and DRM model workspaces
- Report diagrams and generated exports
- AI-assisted mapping with configurable providers
- Experimental status: subject to change

## Project Structure

- `src/HERM-MAPPER-APP/`: ASP.NET Core MVC application
- `tests/HERM-MAPPER-APP.Tests/`: unit tests
- `docker/`: Dockerfile, Docker Compose files, and env-file generation script
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

## Docker

Build the image directly:

```powershell
docker build -f .\docker\Dockerfile -t herm-mapper-app:local .
```

Build loadable image archives for x64 and arm64:

```powershell
.\docker\Build-DockerImages.ps1
```

```sh
sh ./docker/Build-DockerImages.sh
```

The scripts write `docker/images/herm-mapper-app_local_linux-amd64.tar` and `docker/images/herm-mapper-app_local_linux-arm64.tar`. Each archive includes the `herm-mapper-app:local` tag used by the compose files. On a target machine, load the matching archive before running compose:

```powershell
docker load --input .\docker\images\herm-mapper-app_local_linux-amd64.tar
docker compose -f .\docker\docker-compose.sqlite.yml up --no-build
```

```sh
docker load --input ./docker/images/herm-mapper-app_local_linux-amd64.tar
docker compose -f ./docker/docker-compose.sqlite.yml up --no-build
```

To build both archives and load the image matching the local Docker engine:

```powershell
.\docker\Build-DockerImages.ps1 -Load
```

```sh
sh ./docker/Build-DockerImages.sh --load
```

Run with SQLite stored in a Docker volume:

```powershell
.\docker\Convert-AppSettingsToDockerEnv.ps1 -Mode sqlite -Force
docker compose -f .\docker\docker-compose.sqlite.yml up --build
```

Run with an external SQL Server database:

```powershell
.\docker\Convert-AppSettingsToDockerEnv.ps1 -Mode external-db -SqlServerConnectionString "Server=tcp:<sql-server-host>,1433;Database=herm-mapper;User ID=<sql-user>;Password=<sql-password>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True" -Force
docker compose -f .\docker\docker-compose.external-db.yml up --build
```

The app listens on `http://localhost:8080` by default. Set `HERM_HTTP_PORT` in your shell before running Docker Compose, or edit the compose file, to use a different host port. Generated `docker/.env*` files are local-only and should not be committed.

## Reference Models And Artifacts

The catalogue import workflow supports HERM TRM, ARM, BRM, and DRM workbooks. DRM imports use the workbook relationship hierarchy of Topic Type -> Topic -> Entity -> Common Sub-Class, and custom DRM models are stored in their own DRM schema without dependencies on ARM, BRM, TRM, products, services, or applications.

Report diagrams are generated from imported catalogue data and custom model records. The app does not depend on deployed reference model files under a `Model` path; `.local.data/Model` remains local-development input storage only.

## Documentation

### Usage

- [User Guide](docs/user-guide.md)

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

## Attribution

HERM-MAPPER-APP uses Higher Education Reference Model concepts and reference material with attribution to [EUNIS](https://eunis.org/), the European University Information Systems organisation.

## License

See [LICENSE](LICENSE) for details.

---

> **Note:** This project is experimental and may not be production-ready. Contributions and feedback are welcome.
