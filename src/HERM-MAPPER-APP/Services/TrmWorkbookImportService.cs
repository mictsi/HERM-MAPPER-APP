using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HERMMapperApp.Data;
using HERMMapperApp.Infrastructure;
using HERMMapperApp.Models;
using HERMMapperApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Services;

public sealed partial class TrmWorkbookImportService(
    AppDbContext dbContext,
    ComponentVersioningService componentVersioningService,
    AuditLogService auditLogService)
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public async Task<TrmWorkbookVerificationResult> VerifyAsync(
        string workbookPath,
        ReferenceModelKind? modelKind = null,
        CancellationToken cancellationToken = default)
    {
        var fallbackModelKind = modelKind ?? ReferenceModelKind.Trm;

        try
        {
            await using var archive = await ZipFile.OpenReadAsync(workbookPath, cancellationToken);
            var sheetLookup = LoadSheetLookup(archive);
            var resolvedModelKind = ResolveModelKind(sheetLookup, modelKind);
            var snapshot = LoadSnapshot(archive, sheetLookup, resolvedModelKind);
            return await BuildVerificationResultAsync(snapshot, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException)
        {
            return new TrmWorkbookVerificationResult
            {
                ModelKind = fallbackModelKind,
                LayerSummaries = BuildLayerSummaries(fallbackModelKind),
                Errors = [ex.Message]
            };
        }
    }

    public async Task<TrmWorkbookImportSummary> ImportAsync(
        string workbookPath,
        ReferenceModelKind? modelKind = null,
        CancellationToken cancellationToken = default)
    {
        await using var archive = await ZipFile.OpenReadAsync(workbookPath, cancellationToken);
        var sheetLookup = LoadSheetLookup(archive);
        var resolvedModelKind = ResolveModelKind(sheetLookup, modelKind);
        var snapshot = LoadSnapshot(archive, sheetLookup, resolvedModelKind);
        var verification = await BuildVerificationResultAsync(snapshot, cancellationToken);

        if (!verification.IsValid)
        {
            throw new InvalidOperationException("Workbook verification failed. Resolve the reported errors before importing.");
        }

        return await UpsertSnapshotAsync(snapshot, cancellationToken);
    }

    private async Task<TrmWorkbookVerificationResult> BuildVerificationResultAsync(
        CatalogueWorkbookSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var errors = ValidateSnapshot(snapshot);
        var warnings = new List<string>();

        var existingCodes = await LoadExistingCodesAsync(snapshot.ModelKind, cancellationToken);
        var existingDomainCodes = existingCodes.DomainCodes;
        var existingCapabilityCodes = existingCodes.CapabilityCodes;
        var existingComponentCodes = existingCodes.ComponentCodes;

        var existingDomainCodeSet = new HashSet<string>(existingDomainCodes, StringComparer.OrdinalIgnoreCase);
        var existingCapabilityCodeSet = new HashSet<string>(existingCapabilityCodes, StringComparer.OrdinalIgnoreCase);
        var existingComponentCodeSet = new HashSet<string>(existingComponentCodes, StringComparer.OrdinalIgnoreCase);

        if (snapshot.Domains.Count == 0)
        {
            errors.Add($"The workbook does not contain any {ReferenceModelCatalog.GetDomainLabel(snapshot.ModelKind).ToLowerInvariant()} rows.");
        }

        if (snapshot.Capabilities.Count == 0)
        {
            errors.Add($"The workbook does not contain any {ReferenceModelCatalog.GetCapabilityLabel(snapshot.ModelKind).ToLowerInvariant()} rows.");
        }

        if (snapshot.Components.Count == 0)
        {
            errors.Add($"The workbook does not contain any {ReferenceModelCatalog.GetComponentLabel(snapshot.ModelKind).ToLowerInvariant()} rows.");
        }

        if (errors.Count == 0 &&
            existingDomainCodes.Count == 0 &&
            existingCapabilityCodes.Count == 0 &&
            existingComponentCodes.Count == 0)
        {
            warnings.Add($"This import will create the first {ReferenceModelCatalog.GetShortName(snapshot.ModelKind)} model in the database.");
        }

        var domainCountToAdd = snapshot.Domains.Count(x => !existingDomainCodeSet.Contains(x.Code));
        var domainCountToUpdate = snapshot.Domains.Count(x => existingDomainCodeSet.Contains(x.Code));
        var capabilityCountToAdd = snapshot.Capabilities.Count(x => !existingCapabilityCodeSet.Contains(x.Code));
        var capabilityCountToUpdate = snapshot.Capabilities.Count(x => existingCapabilityCodeSet.Contains(x.Code));
        var componentCountToAdd = snapshot.Components.Count(x => !existingComponentCodeSet.Contains(x.Code));
        var componentCountToUpdate = snapshot.Components.Count(x => existingComponentCodeSet.Contains(x.Code));

        return new TrmWorkbookVerificationResult
        {
            ModelKind = snapshot.ModelKind,
            DomainRowCount = snapshot.Domains.Count,
            CapabilityRowCount = snapshot.Capabilities.Count,
            ComponentRowCount = snapshot.Components.Count,
            DomainsToAdd = domainCountToAdd,
            DomainsToUpdate = domainCountToUpdate,
            CapabilitiesToAdd = capabilityCountToAdd,
            CapabilitiesToUpdate = capabilityCountToUpdate,
            ComponentsToAdd = componentCountToAdd,
            ComponentsToUpdate = componentCountToUpdate,
            LayerSummaries = BuildLayerSummaries(
                snapshot.ModelKind,
                snapshot.Domains.Count,
                domainCountToAdd,
                domainCountToUpdate,
                snapshot.Capabilities.Count,
                capabilityCountToAdd,
                capabilityCountToUpdate,
                snapshot.Components.Count,
                componentCountToAdd,
                componentCountToUpdate),
            Errors = errors,
            Warnings = warnings
        };
    }

    private async Task<ExistingReferenceCodes> LoadExistingCodesAsync(
        ReferenceModelKind modelKind,
        CancellationToken cancellationToken)
    {
        return modelKind switch
        {
            ReferenceModelKind.Trm => new ExistingReferenceCodes(
                await dbContext.TrmDomains
                    .AsNoTracking()
                    .ForReferenceModel(ReferenceModelKind.Trm)
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken),
                await dbContext.TrmCapabilities
                    .AsNoTracking()
                    .ForReferenceModel(ReferenceModelKind.Trm)
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken),
                await dbContext.TrmComponents
                    .AsNoTracking()
                    .ForReferenceModel(ReferenceModelKind.Trm)
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken)),
            ReferenceModelKind.Arm => new ExistingReferenceCodes(
                await dbContext.ArmDomains
                    .AsNoTracking()
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken),
                await dbContext.ArmCapabilities
                    .AsNoTracking()
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken),
                await dbContext.ArmComponents
                    .AsNoTracking()
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken)),
            ReferenceModelKind.Brm => new ExistingReferenceCodes(
                await dbContext.BrmDomains
                    .AsNoTracking()
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken),
                await dbContext.BrmCapabilities
                    .AsNoTracking()
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken),
                await dbContext.BrmComponents
                    .AsNoTracking()
                    .Select(x => x.Code)
                    .ToListAsync(cancellationToken)),
            _ => throw new InvalidOperationException($"Unsupported reference model '{modelKind}'.")
        };
    }

    private async Task<TrmWorkbookImportSummary> UpsertSnapshotAsync(
        CatalogueWorkbookSnapshot snapshot,
        CancellationToken cancellationToken) =>
        snapshot.ModelKind switch
        {
            ReferenceModelKind.Trm => await UpsertTrmSnapshotAsync(snapshot, cancellationToken),
            ReferenceModelKind.Arm => await UpsertArmSnapshotAsync(snapshot, cancellationToken),
            ReferenceModelKind.Brm => await UpsertBrmSnapshotAsync(snapshot, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported reference model '{snapshot.ModelKind}'.")
        };

    private async Task<TrmWorkbookImportSummary> UpsertTrmSnapshotAsync(
        CatalogueWorkbookSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var domainsByCode = await dbContext.TrmDomains
            .ForReferenceModel(snapshot.ModelKind)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var domainsAdded = 0;
        var domainsUpdated = 0;
        foreach (var row in snapshot.Domains)
        {
            if (domainsByCode.TryGetValue(row.Code, out var existingDomain))
            {
                existingDomain.SourceTitle = row.SourceTitle;
                existingDomain.Name = row.Name;
                existingDomain.Description = row.Description;
                existingDomain.Comments = row.Comments;
                domainsUpdated++;
                continue;
            }

            var domain = new TrmDomain
            {
                SourceTitle = row.SourceTitle,
                Code = row.Code,
                Name = row.Name,
                Description = row.Description,
                Comments = row.Comments
            };

            dbContext.TrmDomains.Add(domain);
            domainsByCode[row.Code] = domain;
            domainsAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var trackedDomainsByCode = await dbContext.TrmDomains
            .ForReferenceModel(snapshot.ModelKind)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var capabilitiesByCode = await dbContext.TrmCapabilities
            .ForReferenceModel(snapshot.ModelKind)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var capabilitiesAdded = 0;
        var capabilitiesUpdated = 0;
        foreach (var row in snapshot.Capabilities)
        {
            trackedDomainsByCode.TryGetValue(row.ParentDomainCode, out var parentDomain);

            if (capabilitiesByCode.TryGetValue(row.Code, out var existingCapability))
            {
                existingCapability.SourceTitle = row.SourceTitle;
                existingCapability.Name = row.Name;
                existingCapability.ParentDomainCode = row.ParentDomainCode;
                existingCapability.ParentDomainId = parentDomain?.Id;
                existingCapability.Description = row.Description;
                existingCapability.Comments = row.Comments;
                capabilitiesUpdated++;
                continue;
            }

            var capability = new TrmCapability
            {
                SourceTitle = row.SourceTitle,
                Code = row.Code,
                Name = row.Name,
                ParentDomainCode = row.ParentDomainCode,
                ParentDomainId = parentDomain?.Id,
                Description = row.Description,
                Comments = row.Comments
            };

            dbContext.TrmCapabilities.Add(capability);
            capabilitiesByCode[row.Code] = capability;
            capabilitiesAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var trackedCapabilitiesByCode = await dbContext.TrmCapabilities
            .ForReferenceModel(snapshot.ModelKind)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var componentsByCode = await dbContext.TrmComponents
            .Include(x => x.CapabilityLinks)
            .ForReferenceModel(snapshot.ModelKind)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var componentsAdded = 0;
        var componentsUpdated = 0;
        var changedComponentIds = new List<int>();
        var addedComponents = new List<TrmComponent>();
        foreach (var row in snapshot.Components)
        {
            var capabilityIds = row.ParentCapabilityCodes
                .Where(trackedCapabilitiesByCode.ContainsKey)
                .Select(code => trackedCapabilitiesByCode[code].Id)
                .Distinct()
                .ToList();
            var primaryCapabilityCode = row.ParentCapabilityCodes.Count > 0
                ? row.ParentCapabilityCodes[0]
                : null;
            var primaryCapability = primaryCapabilityCode is not null
                ? trackedCapabilitiesByCode[primaryCapabilityCode]
                : null;

            if (componentsByCode.TryGetValue(row.Code, out var existingComponent))
            {
                var changed = existingComponent.SourceTitle != row.SourceTitle ||
                              existingComponent.Name != row.Name ||
                              existingComponent.ParentCapabilityCode != (primaryCapabilityCode ?? string.Empty) ||
                              existingComponent.ParentCapabilityId != primaryCapability?.Id ||
                              existingComponent.Description != row.Description ||
                              existingComponent.Comments != row.Comments ||
                              existingComponent.ProductExamples != row.ProductExamples ||
                              existingComponent.TechnologyComponentCode is not null ||
                              existingComponent.IsCustom;

                existingComponent.SourceTitle = row.SourceTitle;
                existingComponent.Name = row.Name;
                existingComponent.ParentCapabilityCode = primaryCapabilityCode ?? string.Empty;
                existingComponent.ParentCapabilityId = primaryCapability?.Id;
                existingComponent.Description = row.Description;
                existingComponent.Comments = row.Comments;
                existingComponent.ProductExamples = row.ProductExamples;
                existingComponent.TechnologyComponentCode = null;
                existingComponent.IsCustom = false;
                changed |= await SyncCapabilityLinksAsync(existingComponent, capabilityIds, cancellationToken);

                if (changed)
                {
                    componentsUpdated++;
                    changedComponentIds.Add(existingComponent.Id);
                }

                continue;
            }

            var component = new TrmComponent
            {
                SourceTitle = row.SourceTitle,
                Code = row.Code,
                Name = row.Name,
                ParentCapabilityCode = primaryCapabilityCode ?? string.Empty,
                ParentCapabilityId = primaryCapability?.Id,
                Description = row.Description,
                Comments = row.Comments,
                ProductExamples = row.ProductExamples,
                IsCustom = false
            };

            dbContext.TrmComponents.Add(component);
            addedComponents.Add(component);
            foreach (var capabilityId in capabilityIds)
            {
                component.CapabilityLinks.Add(new TrmComponentCapabilityLink
                {
                    TrmCapabilityId = capabilityId,
                    CreatedUtc = DateTime.UtcNow
                });
            }

            componentsByCode[row.Code] = component;
            componentsAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var component in addedComponents)
        {
            await componentVersioningService.RecordVersionAsync(component.Id, "Imported", $"{ReferenceModelCatalog.GetShortName(snapshot.ModelKind)} workbook import", cancellationToken);
        }

        foreach (var componentId in changedComponentIds.Distinct())
        {
            await componentVersioningService.RecordVersionAsync(componentId, "Updated", $"{ReferenceModelCatalog.GetShortName(snapshot.ModelKind)} workbook import", cancellationToken);
        }

        var modelShortName = ReferenceModelCatalog.GetShortName(snapshot.ModelKind);
        await auditLogService.WriteAsync(
            "Reference",
            "Import",
            "TrmWorkbook",
            null,
            $"Imported {modelShortName} workbook: {domainsAdded} {ReferenceModelCatalog.GetDomainLabel(snapshot.ModelKind).ToLowerInvariant()} added, {capabilitiesAdded} {ReferenceModelCatalog.GetCapabilityLabel(snapshot.ModelKind).ToLowerInvariant()} added, {componentsAdded} {ReferenceModelCatalog.GetComponentLabel(snapshot.ModelKind).ToLowerInvariant()} added.",
            $"Updated {domainsUpdated} {ReferenceModelCatalog.GetDomainLabel(snapshot.ModelKind).ToLowerInvariant()}, {capabilitiesUpdated} {ReferenceModelCatalog.GetCapabilityLabel(snapshot.ModelKind).ToLowerInvariant()}, {componentsUpdated} {ReferenceModelCatalog.GetComponentLabel(snapshot.ModelKind).ToLowerInvariant()}.",
            cancellationToken);

        return new TrmWorkbookImportSummary
        {
            ModelKind = snapshot.ModelKind,
            DomainsAdded = domainsAdded,
            DomainsUpdated = domainsUpdated,
            CapabilitiesAdded = capabilitiesAdded,
            CapabilitiesUpdated = capabilitiesUpdated,
            ComponentsAdded = componentsAdded,
            ComponentsUpdated = componentsUpdated,
            LayerSummaries = BuildLayerSummaries(
                snapshot.ModelKind,
                snapshot.Domains.Count,
                domainsAdded,
                domainsUpdated,
                snapshot.Capabilities.Count,
                capabilitiesAdded,
                capabilitiesUpdated,
                snapshot.Components.Count,
                componentsAdded,
                componentsUpdated)
        };
    }

    private async Task<TrmWorkbookImportSummary> UpsertArmSnapshotAsync(
        CatalogueWorkbookSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var domainsByCode = await dbContext.ArmDomains
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var domainsAdded = 0;
        var domainsUpdated = 0;
        foreach (var row in snapshot.Domains)
        {
            if (domainsByCode.TryGetValue(row.Code, out var existingDomain))
            {
                existingDomain.SourceTitle = row.SourceTitle;
                existingDomain.Name = row.Name;
                existingDomain.Description = row.Description;
                existingDomain.Comments = row.Comments;
                domainsUpdated++;
                continue;
            }

            var domain = new ArmDomain
            {
                SourceTitle = row.SourceTitle,
                Code = row.Code,
                Name = row.Name,
                Description = row.Description,
                Comments = row.Comments
            };

            dbContext.ArmDomains.Add(domain);
            domainsByCode[row.Code] = domain;
            domainsAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var trackedDomainsByCode = await dbContext.ArmDomains
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var capabilitiesByCode = await dbContext.ArmCapabilities
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var capabilitiesAdded = 0;
        var capabilitiesUpdated = 0;
        foreach (var row in snapshot.Capabilities)
        {
            trackedDomainsByCode.TryGetValue(row.ParentDomainCode, out var parentDomain);

            if (capabilitiesByCode.TryGetValue(row.Code, out var existingCapability))
            {
                existingCapability.SourceTitle = row.SourceTitle;
                existingCapability.Name = row.Name;
                existingCapability.ParentDomainCode = row.ParentDomainCode;
                existingCapability.ParentDomainId = parentDomain?.Id;
                existingCapability.Description = row.Description;
                existingCapability.Comments = row.Comments;
                capabilitiesUpdated++;
                continue;
            }

            var capability = new ArmCapability
            {
                SourceTitle = row.SourceTitle,
                Code = row.Code,
                Name = row.Name,
                ParentDomainCode = row.ParentDomainCode,
                ParentDomainId = parentDomain?.Id,
                Description = row.Description,
                Comments = row.Comments
            };

            dbContext.ArmCapabilities.Add(capability);
            capabilitiesByCode[row.Code] = capability;
            capabilitiesAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var trackedCapabilitiesByCode = await dbContext.ArmCapabilities
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var componentsByCode = await dbContext.ArmComponents
            .Include(x => x.CapabilityLinks)
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var componentsAdded = 0;
        var componentsUpdated = 0;
        foreach (var row in snapshot.Components)
        {
            var capabilityIds = row.ParentCapabilityCodes
                .Where(trackedCapabilitiesByCode.ContainsKey)
                .Select(code => trackedCapabilitiesByCode[code].Id)
                .Distinct()
                .ToList();
            var primaryCapabilityCode = row.ParentCapabilityCodes.Count > 0
                ? row.ParentCapabilityCodes[0]
                : null;
            var primaryCapability = primaryCapabilityCode is not null
                ? trackedCapabilitiesByCode[primaryCapabilityCode]
                : null;

            if (componentsByCode.TryGetValue(row.Code, out var existingComponent))
            {
                var changed = existingComponent.SourceTitle != row.SourceTitle ||
                              existingComponent.Name != row.Name ||
                              existingComponent.ParentCapabilityCode != (primaryCapabilityCode ?? string.Empty) ||
                              existingComponent.ParentCapabilityId != primaryCapability?.Id ||
                              existingComponent.Description != row.Description ||
                              existingComponent.Comments != row.Comments ||
                              existingComponent.ProductExamples != row.ProductExamples;

                existingComponent.SourceTitle = row.SourceTitle;
                existingComponent.Name = row.Name;
                existingComponent.ParentCapabilityCode = primaryCapabilityCode ?? string.Empty;
                existingComponent.ParentCapabilityId = primaryCapability?.Id;
                existingComponent.Description = row.Description;
                existingComponent.Comments = row.Comments;
                existingComponent.ProductExamples = row.ProductExamples;
                changed |= await SyncArmCapabilityLinksAsync(existingComponent, capabilityIds, cancellationToken);

                if (changed)
                {
                    componentsUpdated++;
                }

                continue;
            }

            var component = new ArmComponent
            {
                SourceTitle = row.SourceTitle,
                Code = row.Code,
                Name = row.Name,
                ParentCapabilityCode = primaryCapabilityCode ?? string.Empty,
                ParentCapabilityId = primaryCapability?.Id,
                Description = row.Description,
                Comments = row.Comments,
                ProductExamples = row.ProductExamples
            };

            dbContext.ArmComponents.Add(component);
            foreach (var capabilityId in capabilityIds)
            {
                component.CapabilityLinks.Add(new ArmComponentCapabilityLink
                {
                    ArmCapabilityId = capabilityId,
                    CreatedUtc = DateTime.UtcNow
                });
            }

            componentsByCode[row.Code] = component;
            componentsAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteImportAuditAsync(snapshot, domainsAdded, domainsUpdated, capabilitiesAdded, capabilitiesUpdated, componentsAdded, componentsUpdated, cancellationToken);
        return BuildImportSummary(snapshot, domainsAdded, domainsUpdated, capabilitiesAdded, capabilitiesUpdated, componentsAdded, componentsUpdated);
    }

    private async Task<TrmWorkbookImportSummary> UpsertBrmSnapshotAsync(
        CatalogueWorkbookSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var domainsByCode = await dbContext.BrmDomains
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var domainsAdded = 0;
        var domainsUpdated = 0;
        foreach (var row in snapshot.Domains)
        {
            if (domainsByCode.TryGetValue(row.Code, out var existingDomain))
            {
                existingDomain.SourceTitle = row.SourceTitle;
                existingDomain.Name = row.Name;
                existingDomain.Description = row.Description;
                existingDomain.Comments = row.Comments;
                domainsUpdated++;
                continue;
            }

            var domain = new BrmDomain
            {
                SourceTitle = row.SourceTitle,
                Code = row.Code,
                Name = row.Name,
                Description = row.Description,
                Comments = row.Comments
            };

            dbContext.BrmDomains.Add(domain);
            domainsByCode[row.Code] = domain;
            domainsAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var trackedDomainsByCode = await dbContext.BrmDomains
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var capabilitiesByCode = await dbContext.BrmCapabilities
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var capabilitiesAdded = 0;
        var capabilitiesUpdated = 0;
        foreach (var row in snapshot.Capabilities)
        {
            trackedDomainsByCode.TryGetValue(row.ParentDomainCode, out var parentDomain);

            if (capabilitiesByCode.TryGetValue(row.Code, out var existingCapability))
            {
                existingCapability.SourceTitle = row.SourceTitle;
                existingCapability.Name = row.Name;
                existingCapability.ParentDomainCode = row.ParentDomainCode;
                existingCapability.ParentDomainId = parentDomain?.Id;
                existingCapability.Description = row.Description;
                existingCapability.Comments = row.Comments;
                capabilitiesUpdated++;
                continue;
            }

            var capability = new BrmCapability
            {
                SourceTitle = row.SourceTitle,
                Code = row.Code,
                Name = row.Name,
                ParentDomainCode = row.ParentDomainCode,
                ParentDomainId = parentDomain?.Id,
                Description = row.Description,
                Comments = row.Comments
            };

            dbContext.BrmCapabilities.Add(capability);
            capabilitiesByCode[row.Code] = capability;
            capabilitiesAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var trackedCapabilitiesByCode = await dbContext.BrmCapabilities
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var componentsByCode = await dbContext.BrmComponents
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var componentsAdded = 0;
        var componentsUpdated = 0;
        foreach (var row in snapshot.Components)
        {
            var primaryCapabilityCode = row.ParentCapabilityCodes.Count > 0
                ? row.ParentCapabilityCodes[0]
                : null;
            var primaryCapability = primaryCapabilityCode is not null
                ? trackedCapabilitiesByCode[primaryCapabilityCode]
                : null;

            if (componentsByCode.TryGetValue(row.Code, out var existingComponent))
            {
                var changed = existingComponent.SourceTitle != row.SourceTitle ||
                              existingComponent.Name != row.Name ||
                              existingComponent.ParentCapabilityCode != (primaryCapabilityCode ?? string.Empty) ||
                              existingComponent.ParentCapabilityId != primaryCapability?.Id ||
                              existingComponent.Description != row.Description ||
                              existingComponent.Comments != row.Comments ||
                              existingComponent.ProductExamples != row.ProductExamples;

                existingComponent.SourceTitle = row.SourceTitle;
                existingComponent.Name = row.Name;
                existingComponent.ParentCapabilityCode = primaryCapabilityCode ?? string.Empty;
                existingComponent.ParentCapabilityId = primaryCapability?.Id;
                existingComponent.Description = row.Description;
                existingComponent.Comments = row.Comments;
                existingComponent.ProductExamples = row.ProductExamples;

                if (changed)
                {
                    componentsUpdated++;
                }

                continue;
            }

            var component = new BrmComponent
            {
                SourceTitle = row.SourceTitle,
                Code = row.Code,
                Name = row.Name,
                ParentCapabilityCode = primaryCapabilityCode ?? string.Empty,
                ParentCapabilityId = primaryCapability?.Id,
                Description = row.Description,
                Comments = row.Comments,
                ProductExamples = row.ProductExamples
            };

            dbContext.BrmComponents.Add(component);
            componentsByCode[row.Code] = component;
            componentsAdded++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await WriteImportAuditAsync(snapshot, domainsAdded, domainsUpdated, capabilitiesAdded, capabilitiesUpdated, componentsAdded, componentsUpdated, cancellationToken);
        return BuildImportSummary(snapshot, domainsAdded, domainsUpdated, capabilitiesAdded, capabilitiesUpdated, componentsAdded, componentsUpdated);
    }

    private static TrmWorkbookImportSummary BuildImportSummary(
        CatalogueWorkbookSnapshot snapshot,
        int domainsAdded,
        int domainsUpdated,
        int capabilitiesAdded,
        int capabilitiesUpdated,
        int componentsAdded,
        int componentsUpdated) =>
        new()
        {
            ModelKind = snapshot.ModelKind,
            DomainsAdded = domainsAdded,
            DomainsUpdated = domainsUpdated,
            CapabilitiesAdded = capabilitiesAdded,
            CapabilitiesUpdated = capabilitiesUpdated,
            ComponentsAdded = componentsAdded,
            ComponentsUpdated = componentsUpdated,
            LayerSummaries = BuildLayerSummaries(
                snapshot.ModelKind,
                snapshot.Domains.Count,
                domainsAdded,
                domainsUpdated,
                snapshot.Capabilities.Count,
                capabilitiesAdded,
                capabilitiesUpdated,
                snapshot.Components.Count,
                componentsAdded,
                componentsUpdated)
        };

    private async Task WriteImportAuditAsync(
        CatalogueWorkbookSnapshot snapshot,
        int domainsAdded,
        int domainsUpdated,
        int capabilitiesAdded,
        int capabilitiesUpdated,
        int componentsAdded,
        int componentsUpdated,
        CancellationToken cancellationToken)
    {
        var modelShortName = ReferenceModelCatalog.GetShortName(snapshot.ModelKind);
        await auditLogService.WriteAsync(
            "Reference",
            "Import",
            "TrmWorkbook",
            null,
            $"Imported {modelShortName} workbook: {domainsAdded} {ReferenceModelCatalog.GetDomainLabel(snapshot.ModelKind).ToLowerInvariant()} added, {capabilitiesAdded} {ReferenceModelCatalog.GetCapabilityLabel(snapshot.ModelKind).ToLowerInvariant()} added, {componentsAdded} {ReferenceModelCatalog.GetComponentLabel(snapshot.ModelKind).ToLowerInvariant()} added.",
            $"Updated {domainsUpdated} {ReferenceModelCatalog.GetDomainLabel(snapshot.ModelKind).ToLowerInvariant()}, {capabilitiesUpdated} {ReferenceModelCatalog.GetCapabilityLabel(snapshot.ModelKind).ToLowerInvariant()}, {componentsUpdated} {ReferenceModelCatalog.GetComponentLabel(snapshot.ModelKind).ToLowerInvariant()}.",
            cancellationToken);
    }

    private static CatalogueWorkbookSnapshot LoadSnapshot(
        ZipArchive archive,
        Dictionary<string, string> sheetLookup,
        ReferenceModelKind modelKind)
    {
        var sharedStrings = LoadSharedStrings(archive);
        return modelKind switch
        {
            ReferenceModelKind.Brm => LoadBrmSnapshot(archive, sheetLookup, sharedStrings),
            _ => LoadHierarchicalSnapshot(archive, sheetLookup, sharedStrings, GetDefinition(modelKind))
        };
    }

    private static CatalogueWorkbookSnapshot LoadHierarchicalSnapshot(
        ZipArchive archive,
        Dictionary<string, string> sheetLookup,
        IReadOnlyList<string> sharedStrings,
        CatalogueModelDefinition definition)
    {
        var domains = ReadRows(archive, GetRequiredSheetPath(sheetLookup, definition.DomainSheetName), sharedStrings)
            .Skip(1)
            .Select(row => new TrmDomainRow(
                GetValue(row, "A"),
                GetValue(row, "B"),
                GetValue(row, "C"),
                GetValue(row, "D"),
                GetValue(row, "E")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToList();

        var capabilities = ReadRows(archive, GetRequiredSheetPath(sheetLookup, definition.CapabilitySheetName), sharedStrings)
            .Skip(1)
            .Select(row => new TrmCapabilityRow(
                GetValue(row, "A"),
                GetValue(row, "B"),
                GetValue(row, "C"),
                ExtractCode(GetValue(row, "D")) ?? string.Empty,
                GetValue(row, "E"),
                GetValue(row, "F")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToList();

        var components = ReadRows(archive, GetRequiredSheetPath(sheetLookup, definition.ComponentSheetName), sharedStrings)
            .Skip(1)
            .Select(row => new TrmComponentRow(
                GetValue(row, "A"),
                GetValue(row, "B"),
                GetValue(row, "C"),
                ExtractCodes(GetValue(row, "D")),
                GetValue(row, "E"),
                GetValue(row, "F"),
                GetValue(row, "G")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToList();

        return new CatalogueWorkbookSnapshot(definition.ModelKind, domains, capabilities, components, []);
    }

    private static CatalogueWorkbookSnapshot LoadBrmSnapshot(
        ZipArchive archive,
        Dictionary<string, string> sheetLookup,
        IReadOnlyList<string> sharedStrings)
    {
        var rows = ReadRows(archive, GetRequiredSheetPath(sheetLookup, "BRM"), sharedStrings)
            .Skip(1)
            .Select(row => new BrmWorkbookRow(
                GetValue(row, "A"),
                GetValue(row, "B"),
                GetValue(row, "C"),
                GetValue(row, "D"),
                GetValue(row, "E"),
                GetValue(row, "F"),
                GetValue(row, "G"),
                ExtractCode(GetValue(row, "H")) ?? string.Empty,
                GetValue(row, "I"),
                GetValue(row, "J"),
                GetValue(row, "K"),
                GetValue(row, "L")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToList();

        var domains = rows
            .GroupBy(BuildBrmDomainKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var sample = group.First();
                return new TrmDomainRow(
                    BuildBrmDomainTitle(sample),
                    BuildBrmDomainCode(group.Key),
                    BuildBrmDomainName(sample),
                    BuildBrmDomainDescription(sample),
                    BuildBrmDomainComments(sample));
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var capabilities = rows
            .Where(x => x.Level == 1)
            .Select(row => new TrmCapabilityRow(
                row.SourceTitle,
                row.Code,
                row.Name,
                BuildBrmDomainCode(BuildBrmDomainKey(row)),
                row.Description,
                BuildBrmComments(row)))
            .ToList();

        var components = rows
            .Where(x => x.Level == 2)
            .Select(row => new TrmComponentRow(
                row.SourceTitle,
                row.Code,
                row.Name,
                string.IsNullOrWhiteSpace(row.ParentCapabilityCode) ? [] : [row.ParentCapabilityCode],
                row.Description,
                BuildBrmComments(row),
                string.Empty))
            .ToList();

        return new CatalogueWorkbookSnapshot(ReferenceModelKind.Brm, domains, capabilities, components, rows);
    }

    private static List<string> ValidateSnapshot(CatalogueWorkbookSnapshot snapshot)
    {
        var errors = new List<string>();
        var definition = GetDefinition(snapshot.ModelKind);

        if (snapshot.ModelKind == ReferenceModelKind.Brm)
        {
            errors.AddRange(ValidateBrmRows(snapshot.BrmRows));
        }

        errors.AddRange(ValidateCodes(snapshot.Domains.Select(x => x.Code), "domain"));
        errors.AddRange(ValidateCodes(snapshot.Capabilities.Select(x => x.Code), "capability"));
        errors.AddRange(ValidateCodes(snapshot.Components.Select(x => x.Code), "component"));

        errors.AddRange(ValidateCodePrefixes(snapshot.Domains.Select(x => x.Code), definition.DomainPrefix, "domain"));
        errors.AddRange(ValidateCodePrefixes(snapshot.Capabilities.Select(x => x.Code), definition.CapabilityPrefix, "capability"));
        errors.AddRange(ValidateCodePrefixes(snapshot.Components.Select(x => x.Code), definition.ComponentPrefix, "component"));

        foreach (var row in snapshot.Domains.Where(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            errors.Add($"Domain {row.Code} is missing a name.");
        }

        foreach (var row in snapshot.Capabilities.Where(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            errors.Add($"Capability {row.Code} is missing a name.");
        }

        foreach (var row in snapshot.Components.Where(x => string.IsNullOrWhiteSpace(x.Name)))
        {
            errors.Add($"Component {row.Code} is missing a name.");
        }

        var domainCodes = snapshot.Domains
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in snapshot.Capabilities.Where(x => string.IsNullOrWhiteSpace(x.ParentDomainCode) || !domainCodes.Contains(x.ParentDomainCode)))
        {
            errors.Add($"Capability {row.Code} references a missing domain code '{row.ParentDomainCode}'.");
        }

        var capabilityCodes = snapshot.Capabilities
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in snapshot.Components.Where(x => x.ParentCapabilityCodes.Count == 0))
        {
            errors.Add($"Component {row.Code} must reference at least one capability code.");
        }

        foreach (var row in snapshot.Components.Where(x => x.ParentCapabilityCodes.Any(code => !capabilityCodes.Contains(code))))
        {
            var missingCodes = row.ParentCapabilityCodes.Where(code => !capabilityCodes.Contains(code));
            errors.Add($"Component {row.Code} references missing capability code(s): {string.Join(", ", missingCodes)}.");
        }

        return errors
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> ValidateBrmRows(IReadOnlyList<BrmWorkbookRow> rows)
    {
        if (rows.Count == 0)
        {
            yield break;
        }

        var levelOneCodes = rows
            .Where(x => x.Level == 1 && !string.IsNullOrWhiteSpace(x.Code))
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!string.Equals(row.Code[..Math.Min(2, row.Code.Length)], "BC", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"BRM capability {row.Code} must use the BC code prefix.";
            }

            if (row.Level is < 1 or > 2)
            {
                yield return $"BRM capability {row.Code} uses unsupported level '{row.RawLevel}'. Only levels 1 and 2 are supported.";
            }

            if (string.IsNullOrWhiteSpace(row.CapabilityType))
            {
                yield return $"BRM capability {row.Code} is missing a capability type.";
            }

            if (row.Level == 1 && !string.IsNullOrWhiteSpace(row.ParentCapabilityCode))
            {
                yield return $"BRM level 1 capability {row.Code} must not reference a parent capability.";
            }

            if (row.Level == 2 && string.IsNullOrWhiteSpace(row.ParentCapabilityCode))
            {
                yield return $"BRM level 2 capability {row.Code} must reference a parent capability.";
            }

            if (row.Level == 2 &&
                !string.IsNullOrWhiteSpace(row.ParentCapabilityCode) &&
                !levelOneCodes.Contains(row.ParentCapabilityCode))
            {
                yield return $"BRM level 2 capability {row.Code} references missing parent capability '{row.ParentCapabilityCode}'.";
            }
        }
    }

    private static IEnumerable<string> ValidateCodes(IEnumerable<string> codes, string entityLabel)
    {
        var normalizedCodes = codes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();

        foreach (var duplicate in normalizedCodes
                     .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1)
                     .Select(x => x.Key)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            yield return $"The workbook contains duplicate {entityLabel} code '{duplicate}'.";
        }
    }

    private static IEnumerable<string> ValidateCodePrefixes(IEnumerable<string> codes, string prefix, string entityLabel)
    {
        foreach (var invalidCode in codes
                     .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            yield return $"The workbook contains {entityLabel} code '{invalidCode}' which does not use the expected '{prefix}' prefix.";
        }
    }

    private static ReferenceModelKind ResolveModelKind(
        Dictionary<string, string> sheetLookup,
        ReferenceModelKind? selectedModelKind)
    {
        if (selectedModelKind.HasValue)
        {
            return selectedModelKind.Value;
        }

        if (sheetLookup.ContainsKey("TRM Domain") &&
            sheetLookup.ContainsKey("TRM Capability") &&
            sheetLookup.ContainsKey("TRM Component"))
        {
            return ReferenceModelKind.Trm;
        }

        if (sheetLookup.ContainsKey("ARM Domain") &&
            sheetLookup.ContainsKey("ARM Capability") &&
            sheetLookup.ContainsKey("ARM Component"))
        {
            return ReferenceModelKind.Arm;
        }

        if (sheetLookup.ContainsKey("BRM"))
        {
            return ReferenceModelKind.Brm;
        }

        throw new InvalidOperationException("The workbook does not match a supported TRM, ARM, or BRM catalogue structure.");
    }

    private static CatalogueModelDefinition GetDefinition(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => new CatalogueModelDefinition(modelKind, "TRM Domain", "TRM Capability", "TRM Component", "TD", "TP", "TC"),
            ReferenceModelKind.Arm => new CatalogueModelDefinition(modelKind, "ARM Domain", "ARM Capability", "ARM Component", "AD", "AP", "AC"),
            ReferenceModelKind.Brm => new CatalogueModelDefinition(modelKind, "BRM", "BRM", "BRM", "BD", "BC", "BC"),
            _ => throw new InvalidOperationException($"Unsupported reference model '{modelKind}'.")
        };

    private static List<WorkbookImportLayerSummary> BuildLayerSummaries(
        ReferenceModelKind modelKind,
        int domainRowCount = 0,
        int domainsToAdd = 0,
        int domainsToUpdate = 0,
        int capabilityRowCount = 0,
        int capabilitiesToAdd = 0,
        int capabilitiesToUpdate = 0,
        int componentRowCount = 0,
        int componentsToAdd = 0,
        int componentsToUpdate = 0)
    {
        return
        [
            new WorkbookImportLayerSummary
            {
                Label = ReferenceModelCatalog.GetDomainLabel(modelKind),
                RowCount = domainRowCount,
                ToAdd = domainsToAdd,
                ToUpdate = domainsToUpdate
            },
            new WorkbookImportLayerSummary
            {
                Label = ReferenceModelCatalog.GetCapabilityLabel(modelKind),
                RowCount = capabilityRowCount,
                ToAdd = capabilitiesToAdd,
                ToUpdate = capabilitiesToUpdate
            },
            new WorkbookImportLayerSummary
            {
                Label = ReferenceModelCatalog.GetComponentLabel(modelKind),
                RowCount = componentRowCount,
                ToAdd = componentsToAdd,
                ToUpdate = componentsToUpdate
            }
        ];
    }

    private static string GetRequiredSheetPath(Dictionary<string, string> sheetLookup, string sheetName)
    {
        if (!sheetLookup.TryGetValue(sheetName, out var worksheetPath))
        {
            throw new InvalidOperationException($"The workbook is missing the required '{sheetName}' sheet.");
        }

        return worksheetPath;
    }

    private static Dictionary<string, string> LoadSheetLookup(ZipArchive archive)
    {
        using var workbookStream = archive.GetEntry("xl/workbook.xml")?.Open()
            ?? throw new InvalidOperationException("The workbook.xml part was not found.");
        using var relationshipsStream = archive.GetEntry("xl/_rels/workbook.xml.rels")?.Open()
            ?? throw new InvalidOperationException("The workbook relationship part was not found.");

        var workbook = XDocument.Load(workbookStream);
        var relationships = XDocument.Load(relationshipsStream);

        var targetsById = relationships.Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .ToDictionary(
                x => x.Attribute("Id")?.Value ?? string.Empty,
                x => x.Attribute("Target")?.Value ?? string.Empty)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return workbook.Root?
            .Element(SpreadsheetNs + "sheets")?
            .Elements(SpreadsheetNs + "sheet")
            .ToDictionary(
                x => x.Attribute("name")?.Value ?? string.Empty,
                x =>
                {
                    var relationshipId = x.Attribute(RelationshipNs + "id")?.Value ?? string.Empty;
                    return $"xl/{targetsById[relationshipId]}";
                },
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> LoadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        return document.Root?
            .Elements(SpreadsheetNs + "si")
            .Select(x => string.Concat(x.Descendants(SpreadsheetNs + "t").Select(t => t.Value)))
            .ToList()
            ?? [];
    }

    private static List<Dictionary<string, string>> ReadRows(
        ZipArchive archive,
        string worksheetPath,
        IReadOnlyList<string> sharedStrings)
    {
        using var stream = archive.GetEntry(worksheetPath)?.Open()
            ?? throw new InvalidOperationException($"The worksheet part '{worksheetPath}' was not found.");
        var document = XDocument.Load(stream);

        return document.Root?
            .Element(SpreadsheetNs + "sheetData")?
            .Elements(SpreadsheetNs + "row")
            .Select(row => row.Elements(SpreadsheetNs + "c")
                .ToDictionary(
                    cell => GetColumnReference(cell.Attribute("r")?.Value),
                    cell => GetCellValue(cell, sharedStrings)))
            .ToList()
            ?? [];
    }

    private static string GetCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var cellType = cell.Attribute("t")?.Value;

        if (string.Equals(cellType, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return cell.Element(SpreadsheetNs + "is")?.Value?.Trim() ?? string.Empty;
        }

        var rawValue = cell.Element(SpreadsheetNs + "v")?.Value?.Trim() ?? string.Empty;
        if (string.Equals(cellType, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex) &&
            sharedIndex >= 0 &&
            sharedIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedIndex];
        }

        return rawValue;
    }

    private static string GetValue(Dictionary<string, string> row, string columnName) =>
        row.TryGetValue(columnName, out var value) ? value.Trim() : string.Empty;

    private static string GetColumnReference(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return string.Empty;
        }

        return new string(cellReference.TakeWhile(char.IsLetter).ToArray());
    }

    private static string? ExtractCode(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var match = TrmCodeRegex().Match(rawValue);
        return match.Success ? match.Value : rawValue.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static List<string> ExtractCodes(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        var matches = TrmCodeRegex()
            .Matches(rawValue)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count > 0)
        {
            return matches;
        }

        return rawValue
            .Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ExtractCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<bool> SyncCapabilityLinksAsync(TrmComponent component, IReadOnlyList<int> capabilityIds, CancellationToken cancellationToken)
    {
        var existingLinks = await dbContext.TrmComponentCapabilityLinks
            .Where(x => x.TrmComponentId == component.Id)
            .ToListAsync(cancellationToken);

        var existingCapabilityIds = existingLinks
            .Select(x => x.TrmCapabilityId)
            .ToHashSet();
        var targetCapabilityIds = capabilityIds
            .ToHashSet();

        var changed = false;

        foreach (var link in existingLinks.Where(x => !targetCapabilityIds.Contains(x.TrmCapabilityId)))
        {
            dbContext.TrmComponentCapabilityLinks.Remove(link);
            changed = true;
        }

        foreach (var capabilityId in capabilityIds.Where(x => !existingCapabilityIds.Contains(x)))
        {
            dbContext.TrmComponentCapabilityLinks.Add(new TrmComponentCapabilityLink
            {
                TrmComponentId = component.Id,
                TrmCapabilityId = capabilityId,
                CreatedUtc = DateTime.UtcNow
            });
            changed = true;
        }

        return changed;
    }

    private async Task<bool> SyncArmCapabilityLinksAsync(ArmComponent component, IReadOnlyList<int> capabilityIds, CancellationToken cancellationToken)
    {
        var existingLinks = await dbContext.ArmComponentCapabilityLinks
            .Where(x => x.ArmComponentId == component.Id)
            .ToListAsync(cancellationToken);

        var existingCapabilityIds = existingLinks
            .Select(x => x.ArmCapabilityId)
            .ToHashSet();
        var targetCapabilityIds = capabilityIds
            .ToHashSet();

        var changed = false;

        foreach (var link in existingLinks.Where(x => !targetCapabilityIds.Contains(x.ArmCapabilityId)))
        {
            dbContext.ArmComponentCapabilityLinks.Remove(link);
            changed = true;
        }

        foreach (var capabilityId in capabilityIds.Where(x => !existingCapabilityIds.Contains(x)))
        {
            dbContext.ArmComponentCapabilityLinks.Add(new ArmComponentCapabilityLink
            {
                ArmComponentId = component.Id,
                ArmCapabilityId = capabilityId,
                CreatedUtc = DateTime.UtcNow
            });
            changed = true;
        }

        return changed;
    }

    private static string BuildBrmDomainKey(BrmWorkbookRow row) =>
        string.Join("|",
            NormalizeBrmDomainPart(row.CapabilityType),
            NormalizeBrmDomainPart(row.ValueChain),
            NormalizeBrmDomainPart(row.ValueChainSegment));

    private static string BuildBrmDomainCode(string domainKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(domainKey));
        return $"BD{Convert.ToHexString(hash)[..8]}";
    }

    private static string BuildBrmDomainTitle(BrmWorkbookRow row)
    {
        var name = BuildBrmDomainName(row);
        return string.IsNullOrWhiteSpace(row.CapabilityType)
            ? name
            : $"{row.CapabilityType} {name}".Trim();
    }

    private static string BuildBrmDomainName(BrmWorkbookRow row)
    {
        var parts = new[]
        {
            row.ValueChain,
            row.ValueChainSegment
        }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        if (parts.Count > 0)
        {
            return string.Join(" / ", parts);
        }

        return string.IsNullOrWhiteSpace(row.CapabilityType)
            ? "General"
            : row.CapabilityType.Trim();
    }

    private static string BuildBrmDomainDescription(BrmWorkbookRow row)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.CapabilityType))
        {
            parts.Add($"Capability type: {row.CapabilityType.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(row.ValueChain))
        {
            parts.Add($"Value chain: {row.ValueChain.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(row.ValueChainSegment))
        {
            parts.Add($"Segment: {row.ValueChainSegment.Trim()}");
        }

        return string.Join(" | ", parts);
    }

    private static string BuildBrmDomainComments(BrmWorkbookRow row) =>
        string.IsNullOrWhiteSpace(row.CapabilityType)
            ? string.Empty
            : row.CapabilityType.Trim();

    private static string BuildBrmComments(BrmWorkbookRow row)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Notes))
        {
            parts.Add(row.Notes.Trim());
        }

        if (!string.IsNullOrWhiteSpace(row.Assessment))
        {
            parts.Add($"Assessment: {row.Assessment.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(row.DisplaySequence))
        {
            parts.Add($"Display sequence: {row.DisplaySequence.Trim()}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string NormalizeBrmDomainPart(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim().ToUpperInvariant();

    [GeneratedRegex(@"[A-Z]{2}\d{3}", RegexOptions.CultureInvariant)]
    private static partial Regex TrmCodeRegex();

    private sealed record CatalogueModelDefinition(
        ReferenceModelKind ModelKind,
        string DomainSheetName,
        string CapabilitySheetName,
        string ComponentSheetName,
        string DomainPrefix,
        string CapabilityPrefix,
        string ComponentPrefix);

    private sealed record ExistingReferenceCodes(
        IReadOnlyList<string> DomainCodes,
        IReadOnlyList<string> CapabilityCodes,
        IReadOnlyList<string> ComponentCodes);

    private sealed record CatalogueWorkbookSnapshot(
        ReferenceModelKind ModelKind,
        IReadOnlyList<TrmDomainRow> Domains,
        IReadOnlyList<TrmCapabilityRow> Capabilities,
        IReadOnlyList<TrmComponentRow> Components,
        IReadOnlyList<BrmWorkbookRow> BrmRows);

    private sealed record TrmDomainRow(
        string SourceTitle,
        string Code,
        string Name,
        string Description,
        string Comments);

    private sealed record TrmCapabilityRow(
        string SourceTitle,
        string Code,
        string Name,
        string ParentDomainCode,
        string Description,
        string Comments);

    private sealed record TrmComponentRow(
        string SourceTitle,
        string Code,
        string Name,
        IReadOnlyList<string> ParentCapabilityCodes,
        string Description,
        string Comments,
        string ProductExamples);

    private sealed record BrmWorkbookRow(
        string SourceTitle,
        string CapabilityType,
        string RawLevel,
        string ValueChain,
        string ValueChainSegment,
        string Code,
        string Name,
        string ParentCapabilityCode,
        string Description,
        string Notes,
        string Assessment,
        string DisplaySequence)
    {
        public int Level => int.TryParse(RawLevel, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
            ? level
            : -1;
    }
}
