# Azure App Service Deployment

Two deployment scripts exist under `scripts/`:

- `deploy-appservice.ps1`: original script with legacy behavior
- `deploy-appservice-azcli.ps1`: recommended script using Azure CLI

## Recommended Script

`deploy-appservice-azcli.ps1`:

- Uses an existing App Service plan specified by `-AppPlan`
- Creates the Web App if it does not exist
- Publishes the project under `src/HERM-MAPPER-APP`
- Zips the publish output and deploys with `az webapp deploy`
- Loads an appsettings JSON file, flattens nested keys with `Section__Key`, and applies them as App Settings

## Usage

Run from the repository root:

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

## Notes

- The resource group must already exist because the script derives the region from it.
- `deploy-appservice-azcli.ps1` requires `az` CLI and `dotnet` SDK on `PATH`.
- Settings are flattened into App Service environment variables using `Section__Key` naming.

## Release Packaging

`./build.ps1 -Target Prod -Runtime All` produces runtime-specific binaries under `artifacts/prod`.

The GitHub release workflow packages each runtime directory as a separate zip asset.

## Related Docs

- [Configuration Guide](configuration.md)
- [Development Guide](development.md)
