# HERM Mapper User Guide

## What the App Does

HERM Mapper is an ASP.NET Core MVC web application for curating Higher Education Reference Models in one workspace. It helps an institution maintain product, service, application, and business capability catalogues, then connect those records to the HERM model layers:

- TRM, the Technology Reference Model, classifies products against technology domains, capabilities, and components.
- ARM, the Application Reference Model, links applications to ARM components and the TRM products that support them.
- BRM, the Business Reference Model, groups business capability work into BRM model workspaces and links capabilities through ARM to applications and products.

The app also provides a searchable HERM browser, dashboards, visual diagrams, imports, exports, AI-assisted product mapping, user management, restore tools, and an audit change log.

## User Roles

- Viewer: can sign in, browse catalogues, open the HERM browser, and view reports.
- Contributor: can do Viewer tasks and maintain catalogue records such as products, services, applications, BRM models, and BRM capabilities.
- Administrator: can do everything, including TRM mappings, imports, exports, users, configuration, AI configuration, restore tools, and the change log.

## Start the App

From the repository root:

```powershell
dotnet restore .\HERM-MAPPER-APP.sln
dotnet build .\HERM-MAPPER-APP.sln
dotnet run --project .\src\HERM-MAPPER-APP\HERM-MAPPER-APP.csproj
```

Open the local URL shown in the terminal. The launch settings define `http://localhost:5143` and `https://localhost:7178` for local profiles.

On first launch, the app creates its database and supporting tables. SQLite is the default database provider unless configuration selects SQL Server.

## First Sign-In

The app supports local accounts by default and can also be configured for OpenID Connect. When the local user table is empty, startup creates one bootstrap administrator.

Before first run, set `Security:BootstrapAdmin:Password` or `HERM_Security__BootstrapAdmin__Password` to the initial admin password. If the setting is absent, the code fallback is `ChangeMeNow!123`; change it immediately after signing in. Do not leave a copied configuration template with an empty password value for real use.

Default bootstrap account values when not configured:

- User name: `admin`
- Email: `admin@local`
- Role: `Administrator`

Local users can change their own password from `Profile`. Administrators can create users, assign roles, reset passwords, and delete users from `Admin > Users`.

## Recommended Setup Flow

1. Sign in as an administrator.
2. Open `Admin > Configuration` and review the display time zone.
3. Add or reorder allowed owner and lifecycle status values.
4. Open `Admin > Import data`.
5. Import the HERM reference workbook for TRM, ARM, or BRM, or configure the remote SQL import source.
6. Add products, then create TRM mappings for them.
7. Add services and connect their products.
8. Add applications and link ARM components to mapped products.
9. Add BRM models and BRM capabilities, then connect those capabilities to ARM.
10. Use reports and exports to review coverage.

## Main Navigation

### Dashboard

The dashboard is the landing page after sign-in. It shows counts for products, completed mappings, and loaded BRM, ARM, and TRM reference data. Use it as a quick entry point to the mapping board, new product, new application, new BRM model, or export data when your role allows those actions.

### HERM Browser

Use `HERM Browser` to search and browse imported TRM, ARM, and BRM reference objects. The left tree lets you drill from model to domain to capability. The results table shows matching components, capabilities, domains, product examples, and component history when available.

### Products

Use `TRM Model > Products` to manage the product catalogue.

Typical product workflow:

1. Select `Add product`.
2. Enter name, version, vendor, one or more owners, lifecycle status, description, and notes.
3. Save the product.
4. Open the product details page.
5. As an administrator, add a TRM mapping manually or use `Add mappings with AI` if AI lookup is configured.
6. Use `Visualize` or `Show dependencies` to inspect the product in the model context.

The product list supports search, owner filters, lifecycle filters, bulk selection, and bulk edits for vendor, owners, and lifecycle status.

### Mapping Board

Administrators use `TRM Model > Mapping Board` to review and complete product-to-TRM mappings. The board can filter by search text, mapping status, domain, and capability.

To create or edit a mapping:

1. Open a product details page or mapping record.
2. Select the TRM domain, capability, and component.
3. If the model needs an extension, enter a custom technology component code and custom component name under the selected capability.
4. Set the mapping status, such as Draft, In review, Complete, or Out of scope.
5. Add mapping rationale and save.

Completed mappings are available for export from `Reports > Export data`.

### Services

Use `TRM Model > Services` to define services and the products that make up their flows.

Typical service workflow:

1. Select `Add service`.
2. Enter name, owner, lifecycle status, Asset Criticality Score, and description.
3. Save the service.
4. Open `Connect products`.
5. Search the product palette and drag or click products onto the canvas.
6. Use `Connect` on one product node, then click another node to create or remove a connection.
7. Move nodes, run auto layout if useful, and save connected products.
8. Use `Visualize` to inspect the service flow.

The Asset Criticality Score is a user-defined 1 to 5 score where 1 is lowest criticality and 5 is highest criticality.

### Applications

Use `ARM Model > Applications` to create application records and connect them to the ARM and TRM layers.

Typical application workflow:

1. Select `Add application`.
2. Enter application name, description, and notes.
3. Add one or more ARM-to-product mapping rows.
4. Select an ARM component.
5. Select the supporting product.
6. Select the correct TRM component if the product has multiple TRM mappings.
7. Use `+` to add another row or `-` to remove one.
8. Save the application.

Application details show ARM components, linked products, resolved paths, and dependencies.

### BRM Models and Capabilities

Use `BRM Model > BRM Models` to maintain business reference model workspaces.

Typical BRM workflow:

1. Select `Add BRM model`.
2. Enter name, area, status, and description.
3. Save the model.
4. Open the BRM model details page.
5. Select `Add capability`.
6. Choose the BRM capability inside that model.
7. Add description and notes.
8. Map the capability to ARM by selecting ARM component and ARM capability rows.
9. Save the capability.

The BRM model details page can then show a dependency map through ARM, applications, TRM products, and product endpoints when enough links exist.

### Reports

Use `Reports` to inspect diagrams and analytics:

- ARM diagram for all objects
- ARM diagram per application
- BRM diagram
- TRM diagram for all objects
- TRM diagram per service
- TRM mapping by owner
- TRM Sankey view
- Products by owner
- Owner technology flow
- Incoming connections heatmap
- Incoming connections
- Lifecycle status
- Export data

Administrators can export completed mappings, applications, services, and BRM models in CSV, JSON, or XLSX format.

## Admin Workflows

### Configuration

Use `Admin > Configuration` to set the display time zone and manage configurable field values. The configurable fields currently include owners and lifecycle statuses. Drag values to reorder them, edit values inline, add new values, or remove values that should no longer be offered.

### Import Data

Use `Admin > Import data` for three import paths:

- Remote SQL import: configure a SQL Server source, test the connection and required schema, save settings, run manually, or schedule automatic imports. The expected source schema is documented in `docs/remote-sql-import-source-schema.sql`.
- Product CSV import: upload a semicolon-separated CSV with `MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT`. The app verifies rows before applying them.
- Catalogue workbook import: upload a HERM workbook for TRM, ARM, or BRM. The app verifies the proposed domain, capability, and component changes before importing.

Always review verification results before importing. Imports are blocked when validation errors are present.

### AI Configuration

Use `Admin > AI configuration` to configure AI-assisted product mapping.

Basic AI setup:

1. Select `New provider`.
2. Choose provider type: OpenAI-compatible API, Azure AI Foundry, or Azure AI Foundry Agent.
3. Enter provider name, endpoint, model or deployment, timeout, API key, token pricing if you want cost estimates, system prompt, and prompt template.
4. Save the provider.
5. Enable one provider.
6. Turn on the global `AI lookup` toggle.

When AI lookup is ready, product details pages show an enabled `Add mappings with AI` button. Suggestions below 80 percent confidence are shown but are not preselected.

### Restore Tools

Administrator restore pages are available for deleted products, services, applications, BRM models, and reference model components. Use these pages to review deleted records and restore them. Some restore pages also expose permanent delete actions.

### Change Log

Use `Admin > Change Log` to search audit entries for imports, product updates, mapping activity, component changes, users, and other operational actions. Search supports user, category, action, entity, summary, and details.

## Practical Usage Patterns

- Start with reference data. Product mappings, applications, and BRM dependency maps are only useful when TRM, ARM, and BRM reference data has been imported.
- Add products before mapping. TRM mappings are attached to products.
- Complete product mappings before applications. Application mappings depend on products that already resolve to TRM components.
- Use services for product flow diagrams. Services are best for showing product-to-product dependencies and branching flows.
- Use BRM models for business views. BRM capabilities connect downstream to ARM, applications, TRM, and products.
- Keep lifecycle and owner values clean in configuration. Those values drive filters, reports, and bulk edits.
- Use exports for handoff. Completed mappings preserve the product CSV shape, and broader exports support CSV, JSON, and XLSX.

## Related Documentation

- [Installation Guide](installation.md)
- [Configuration Guide](configuration.md)
- [Development Guide](development.md)
- [AI Overview](ai.md)
- [Azure AI Foundry Agent Setup](azure-ai-foundry-agent.md)
- [Azure App Service Deployment](deployment-appservice.md)
