# Configuration Guide

## Configuration Files

- `src/HERM-MAPPER-APP/appsettings.json`
- `src/HERM-MAPPER-APP/appsettings.Development.json`

## Database Settings

Database provider can be selected with:

- `Database:Provider`
- `HERM_Database__Provider`

SQLite can be configured with:

- `Database:SqliteFilePath`
- `ConnectionStrings:Sqlite`
- `ConnectionStrings:DefaultConnection`

SQL Server can be configured with:

- `Database:ConnectionString`
- `ConnectionStrings:SqlServer`
- `ConnectionStrings:DefaultConnection`

SQLite path tokens supported by the app:

- `|DataDirectory|`
- `|HomeDirectory|`

`|HomeDirectory|/data/...` is suitable for durable Azure App Service storage.

## Diagnostics

Console logging can be controlled with:

- `Diagnostics:Console:*`
- `HERM_Diagnostics__Console__*`

SQL command logging can be controlled with:

- `Diagnostics:Sql:*`
- `HERM_Diagnostics__Sql__*`

## Example Environment Variables

```powershell
$env:HERM_Database__Provider = "SqlServer"
$env:HERM_Database__ConnectionString = "Server=localhost;Database=HermMapper;Trusted_Connection=True;TrustServerCertificate=True"
$env:HERM_Diagnostics__Sql__Enabled = "true"
$env:HERM_Diagnostics__Sql__LogLevel = "Information"
```

## Identity and AI

- Microsoft Entra setup: [entra-app-registration-setup.md](entra-app-registration-setup.md)
- AI provider and Foundry Agent setup: [ai.md](ai.md)

## Related Docs

- [Installation Guide](installation.md)
- [Development Guide](development.md)
