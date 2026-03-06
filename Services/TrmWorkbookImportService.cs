using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Services;

public sealed partial class TrmWorkbookImportService(AppDbContext dbContext)
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public async Task ImportAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        if (await dbContext.TrmDomains.AnyAsync(cancellationToken))
        {
            return;
        }

        using var archive = ZipFile.OpenRead(workbookPath);
        var sharedStrings = LoadSharedStrings(archive);
        var sheetLookup = LoadSheetLookup(archive);

        var domains = ReadRows(archive, sheetLookup["TRM Domain"], sharedStrings)
            .Skip(1)
            .Select(row => new TrmDomain
            {
                SourceTitle = GetValue(row, "A"),
                Code = GetValue(row, "B"),
                Name = GetValue(row, "C"),
                Description = GetValue(row, "D"),
                Comments = GetValue(row, "E")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToList();

        dbContext.TrmDomains.AddRange(domains);
        await dbContext.SaveChangesAsync(cancellationToken);

        var domainIdByCode = await dbContext.TrmDomains
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);

        var capabilities = ReadRows(archive, sheetLookup["TRM Capability"], sharedStrings)
            .Skip(1)
            .Select(row => new TrmCapability
            {
                SourceTitle = GetValue(row, "A"),
                Code = GetValue(row, "B"),
                Name = GetValue(row, "C"),
                ParentDomainCode = ExtractCode(GetValue(row, "D")),
                Description = GetValue(row, "E"),
                Comments = GetValue(row, "F")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToList();

        foreach (var capability in capabilities)
        {
            if (capability.ParentDomainCode is not null &&
                domainIdByCode.TryGetValue(capability.ParentDomainCode, out var domainId))
            {
                capability.ParentDomainId = domainId;
            }
        }

        dbContext.TrmCapabilities.AddRange(capabilities);
        await dbContext.SaveChangesAsync(cancellationToken);

        var capabilityIdByCode = await dbContext.TrmCapabilities
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);

        var components = ReadRows(archive, sheetLookup["TRM Component"], sharedStrings)
            .Skip(1)
            .Select(row => new TrmComponent
            {
                SourceTitle = GetValue(row, "A"),
                Code = GetValue(row, "B"),
                Name = GetValue(row, "C"),
                ParentCapabilityCode = ExtractCode(GetValue(row, "D")),
                Description = GetValue(row, "E"),
                Comments = GetValue(row, "F"),
                ProductExamples = GetValue(row, "G")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .ToList();

        foreach (var component in components)
        {
            if (component.ParentCapabilityCode is not null &&
                capabilityIdByCode.TryGetValue(component.ParentCapabilityCode, out var capabilityId))
            {
                component.ParentCapabilityId = capabilityId;
            }
        }

        dbContext.TrmComponents.AddRange(components);
        await dbContext.SaveChangesAsync(cancellationToken);
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

    private static IEnumerable<Dictionary<string, string>> ReadRows(
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

    private static string GetValue(IReadOnlyDictionary<string, string> row, string columnName) =>
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

    [GeneratedRegex(@"^[A-Z]{2}\d{3}", RegexOptions.CultureInvariant)]
    private static partial Regex TrmCodeRegex();
}
