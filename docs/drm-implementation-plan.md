# DRM Model Implementation Plan

## Summary
Implement DRM as an imported reference model and as a custom modeling workflow, matching the BRM user experience while keeping DRM independent from TRM, ARM, and BRM mappings. Imported DRM data powers catalogue import, browse/search, dashboard counts, and report templates. Custom DRM models let users create multiple DRM model instances using the DRM hierarchy from the catalogue workbook: Topic Type -> Topic -> Entity -> Common Sub-Class.

## Key Changes
- Add `ReferenceModelKind.Drm` with labels `DRM`, `Data Reference Model`, `Topic types`, `Topics`, `Entities`, and `Common sub-classes`; prefixes `DY`, `DT`, and `DE`.
- Add imported DRM entities and DbSets:
  - `DrmTopicType` for `Topic Type` rows.
  - `DrmTopic` for `Topic` rows.
  - `DrmEntity` for `Entity` rows.
  - `DrmCommonSubClass` for `Common Sub-Class` rows.
  - Store alternative names, descriptions, comments, TOGAF metadata, parent topic/entity relationships, and soft-delete fields on entity/sub-class records.
- Add custom DRM model entities with no dependencies on other reference models:
  - `DrmModel` with BRM-equivalent fields: name, area, description, status, soft-delete, created/updated timestamps.
  - Custom DRM item tables that reference only DRM catalogue rows, allowing a model to select topic types, topics, entities, and common sub-classes from the imported DRM schema.
- Add no-migrations schema support in `DatabaseInitializer` for SQLite and SQL Server:
  - Imported tables: `DrmTopicTypes`, `DrmTopics`, `DrmEntities`, `DrmCommonSubClasses`.
  - Custom model tables: `DrmModels` and DRM model selection/detail tables.
  - Match existing BRM table/index/soft-delete conventions.
- Extend catalogue import:
  - Detect DRM workbooks by sheets `Topic Type`, `Topic`, `Entity`, and optional `Common Sub-Class`.
  - Import topic types, topics, entities, and common subclasses using the relationships described in `HERM-DRM-V320-catalogue.xlsx`.
  - Validate duplicate codes, invalid prefixes, missing names, missing topic type/topic parents, missing parent entities, and duplicate `DE` codes across entities/common sub-classes.
  - Reuse the existing summary cards as topic types, topics, and data entities.
- Extend HERM Browser:
  - Add DRM to the model selector/tree.
  - Browse hierarchy as Topic Type -> Topic -> Entity -> Common Sub-Class.
  - Show relationship context and type labels.
  - Add soft delete, restore, and permanent delete for imported DRM entities and common sub-classes, matching BRM reference delete behavior.
- Add custom DRM UI:
  - Add `DrmModelsController` and views equivalent to `BrmModelsController`.
  - Add DRM model editing screens that let users build a custom DRM model from imported DRM topic types, topics, entities, and common sub-classes.
  - Reuse BRM model statuses exactly: `Draft`, `Proposal`, `In Review`, `Pilot`, `Production`, `Retired`.
- Extend reports and artifacts:
  - Add a DRM report page equivalent to the BRM model report.
  - Add `scope=drm` and `drmModelId` support through report routes, diagram generation, and SVG preview.
  - Generate DRM report diagrams from imported catalogue data and custom DRM model records; do not depend on draw.io, ArchiMate, PDF, or workbook files at runtime.
  - Do not expose DRM reference model artifact downloads from the DRM report page.
- Extend dashboard/export/docs:
  - Add DRM imported counts and custom DRM model counts to dashboard/report surfaces.
  - Add DRM custom model export equivalent to BRM model export, without ARM mapping columns.
  - Update `README.md` and `docs/user-guide.md` for DRM import, custom DRM models, browser support, generated reports, and the absence of runtime model-file dependencies.

## Test Plan
- Add importer tests for valid DRM workbook import, missing required DRM sheets, duplicate `DE` codes across entity/sub-class sheets, missing parent topic, missing parent entity, invalid prefixes, and duplicate codes.
- Add schema/startup tests confirming DRM EF mappings and initializer-created SQLite/SQL Server table definitions.
- Add controller tests for selecting DRM during catalogue import, browsing/searching DRM rows, soft delete/restore/permanent delete of imported DRM entities/common sub-classes, and custom DRM model CRUD.
- Add custom DRM model tests confirming records reference only DRM tables and do not require ARM, BRM, TRM, products, services, or applications.
- Add report tests for DRM model selection, generated DRM diagrams, and dashboard DRM counts.
- Validate with:
  - `dotnet test .\tests\HERM-MAPPER-APP.Tests\HERM-MAPPER-APP.Tests.csproj -nologo`
  - `dotnet build .\HERM-MAPPER-APP.sln -nologo`

## Assumptions
- DRM custom models use only the DRM schema and do not map data entities to ARM components/capabilities.
- Imported DRM reference data is separate from custom DRM models; custom models reference imported DRM rows but do not edit them.
- Common sub-classes are modeled as their own imported table and browser/report level below entity.
- The DRM changelog is not database-modeled and is not added as a download unless explicitly requested later.
- Existing TRM, ARM, and BRM imports, reports, browser behavior, and custom BRM workflows must remain compatible.
