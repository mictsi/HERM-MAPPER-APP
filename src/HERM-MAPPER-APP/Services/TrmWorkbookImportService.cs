using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HERMMapperApp.Data;
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

    public async Task<TrmWorkbookVerificationResult> VerifyAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var archive = await ZipFile.OpenReadAsync(workbookPath, cancellationToken);
            var snapshot = LoadSnapshot(archive);
            return await BuildVerificationResultAsync(snapshot, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException)
        {
            return new TrmWorkbookVerificationResult
            {
                Errors = [ex.Message]
            };
        }
    }

    public async Task<TrmWorkbookImportSummary> ImportAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        await using var archive = await ZipFile.OpenReadAsync(workbookPath, cancellationToken);
        var snapshot = LoadSnapshot(archive);
        var verification = await BuildVerificationResultAsync(snapshot, cancellationToken);

        if (!verification.IsValid)
        {
            throw new InvalidOperationException("Workbook verification failed. Resolve the reported errors before importing.");
        }

        return await UpsertSnapshotAsync(snapshot, cancellationToken);
    }

    private async Task<TrmWorkbookVerificationResult> BuildVerificationResultAsync(
        TrmWorkbookSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var errors = ValidateSnapshot(snapshot);
        var warnings = new List<string>();

        var existingDomainCodes = await dbContext.TrmDomains
            .AsNoTracking()
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);
        var existingCapabilityCodes = await dbContext.TrmCapabilities
            .AsNoTracking()
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);
        var existingComponentCodes = await dbContext.TrmComponents
            .AsNoTracking()
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);

        var existingDomainCodeSet = new HashSet<string>(existingDomainCodes, StringComparer.OrdinalIgnoreCase);
        var existingCapabilityCodeSet = new HashSet<string>(existingCapabilityCodes, StringComparer.OrdinalIgnoreCase);
        var existingComponentCodeSet = new HashSet<string>(existingComponentCodes, StringComparer.OrdinalIgnoreCase);

        if (snapshot.Domains.Count == 0)
        {
            errors.Add("The workbook does not contain any TRM domain rows.");
        }

        if (snapshot.Capabilities.Count == 0)
        {
            errors.Add("The workbook does not contain any TRM capability rows.");
        }

        if (snapshot.Components.Count == 0)
        {
            errors.Add("The workbook does not contain any TRM component rows.");
        }

        if (errors.Count == 0 && existingDomainCodes.Count == 0 && existingCapabilityCodes.Count == 0 && existingComponentCodes.Count == 0)
        {
            warnings.Add("This import will create the first TRM model in the database.");
        }

        return new TrmWorkbookVerificationResult
        {
            DomainRowCount = snapshot.Domains.Count,
            CapabilityRowCount = snapshot.Capabilities.Count,
            ComponentRowCount = snapshot.Components.Count,
            DomainsToAdd = snapshot.Domains.Count(x => !existingDomainCodeSet.Contains(x.Code)),
            DomainsToUpdate = snapshot.Domains.Count(x => existingDomainCodeSet.Contains(x.Code)),
            CapabilitiesToAdd = snapshot.Capabilities.Count(x => !existingCapabilityCodeSet.Contains(x.Code)),
            CapabilitiesToUpdate = snapshot.Capabilities.Count(x => existingCapabilityCodeSet.Contains(x.Code)),
            ComponentsToAdd = snapshot.Components.Count(x => !existingComponentCodeSet.Contains(x.Code)),
            ComponentsToUpdate = snapshot.Components.Count(x => existingComponentCodeSet.Contains(x.Code)),
            Errors = errors,
            Warnings = warnings
        };
    }

    private async Task<TrmWorkbookImportSummary> UpsertSnapshotAsync(
        TrmWorkbookSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var domainsByCode = await dbContext.TrmDomains
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
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var capabilitiesByCode = await dbContext.TrmCapabilities
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
            .ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var componentsByCode = await dbContext.TrmComponents
            .Include(x => x.CapabilityLinks)
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
            await componentVersioningService.RecordVersionAsync(component.Id, "Imported", "Workbook import", cancellationToken);
        }

        foreach (var componentId in changedComponentIds.Distinct())
        {
            await componentVersioningService.RecordVersionAsync(componentId, "Updated", "Workbook import", cancellationToken);
        }

        await auditLogService.WriteAsync(
            "Reference",
            "Import",
            "TrmWorkbook",
            null,
            $"Imported TRM workbook: {domainsAdded} domains added, {capabilitiesAdded} capabilities added, {componentsAdded} components added.",
            $"Updated {domainsUpdated} domains, {capabilitiesUpdated} capabilities, {componentsUpdated} components.",
            cancellationToken);

        return new TrmWorkbookImportSummary
        {
            DomainsAdded = domainsAdded,
            DomainsUpdated = domainsUpdated,
            CapabilitiesAdded = capabilitiesAdded,
            CapabilitiesUpdated = capabilitiesUpdated,
            ComponentsAdded = componentsAdded,
            ComponentsUpdated = componentsUpdated
        };
    }

    private static TrmWorkbookSnapshot LoadSnapshot(ZipArchive archive)
    {
        var sharedStrings = LoadSharedStrings(archive);
        var sheetLookup = LoadSheetLookup(archive);

        var domains = ReadRows(archive, GetRequiredSheetPath(sheetLookup, "TRM Domain"), sharedStrings)
            .Skip(1)
            .Select(row => new TrmDomainRow(
                GetValue(row, "A"),
                GetValue(row, "B"),
                GetValue(row, "C"),
                GetValue(row, "D"),
                GetValue(row, "E")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToList();

        var capabilities = ReadRows(archive, GetRequiredSheetPath(sheetLookup, "TRM Capability"), sharedStrings)
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

        var components = ReadRows(archive, GetRequiredSheetPath(sheetLookup, "TRM Component"), sharedStrings)
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

        return new TrmWorkbookSnapshot(domains, capabilities, components);
    }

    private static List<string> ValidateSnapshot(TrmWorkbookSnapshot snapshot)
    {
        var errors = new List<string>();

        errors.AddRange(ValidateCodes(snapshot.Domains.Select(x => x.Code), "domain"));
        errors.AddRange(ValidateCodes(snapshot.Capabilities.Select(x => x.Code), "capability"));
        errors.AddRange(ValidateCodes(snapshot.Components.Select(x => x.Code), "component"));

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
            errors.Add($"Capability {row.Code} references a missing TRM domain code '{row.ParentDomainCode}'.");
        }

        var capabilityCodes = snapshot.Capabilities
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in snapshot.Components.Where(x => x.ParentCapabilityCodes.Count == 0))
        {
            errors.Add($"Component {row.Code} must reference at least one TRM capability code.");
        }

        foreach (var row in snapshot.Components.Where(x => x.ParentCapabilityCodes.Any(code => !capabilityCodes.Contains(code))))
        {
            var missingCodes = row.ParentCapabilityCodes.Where(code => !capabilityCodes.Contains(code));
            errors.Add($"Component {row.Code} references missing TRM capability code(s): {string.Join(", ", missingCodes)}.");
        }

        return errors
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        return rawValue
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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

    [GeneratedRegex(@"^[A-Z]{2}\d{3}", RegexOptions.CultureInvariant)]
    private static partial Regex TrmCodeRegex();

    private sealed record TrmWorkbookSnapshot(
        IReadOnlyList<TrmDomainRow> Domains,
        IReadOnlyList<TrmCapabilityRow> Capabilities,
        IReadOnlyList<TrmComponentRow> Components);

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
}
