using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace HERMMapperApp.Services;

public sealed record TabularExportColumn(string Key, string Header);

public sealed class TabularExportTable(
    string sheetName,
    IReadOnlyList<TabularExportColumn> columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> rows)
{
    public string SheetName { get; } = sheetName;
    public IReadOnlyList<TabularExportColumn> Columns { get; } = columns;
    public IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows { get; } = rows;
}

public static class TabularExportService
{
    private const string SpreadsheetContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string BuildCsv(TabularExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(";", table.Columns.Select(column => column.Header)));

        foreach (var row in table.Rows)
        {
            builder.AppendLine(string.Join(";", table.Columns.Select(column => EscapeCsv(GetValue(row, column.Key)))));
        }

        return builder.ToString();
    }

    public static string BuildJson(TabularExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var rows = table.Rows
            .Select(row => table.Columns.ToDictionary(
                column => column.Key,
                column => GetValue(row, column.Key),
                StringComparer.Ordinal))
            .ToList();

        return JsonSerializer.Serialize(rows, JsonOptions);
    }

    public static byte[] BuildXlsx(TabularExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", BuildContentTypesDocument());
            WriteEntry(archive, "_rels/.rels", BuildRootRelationshipsDocument());
            WriteEntry(archive, "xl/workbook.xml", BuildWorkbookDocument(table.SheetName));
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsDocument());
            WriteEntry(archive, "xl/styles.xml", BuildStylesDocument());
            WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetDocument(table));
        }

        return stream.ToArray();
    }

    public static string GetSpreadsheetContentType() => SpreadsheetContentType;

    private static string? GetValue(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value : null;

    private static string EscapeCsv(string? value)
    {
        var normalized = value ?? string.Empty;
        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }

    private static void WriteEntry(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static XDocument BuildContentTypesDocument()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ns + "Types",
                new XElement(ns + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ns + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ns + "Override",
                    new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ns + "Override",
                    new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                new XElement(ns + "Override",
                    new XAttribute("PartName", "/xl/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))));
    }

    private static XDocument BuildRootRelationshipsDocument()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ns + "Relationships",
                new XElement(ns + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static XDocument BuildWorkbookDocument(string sheetName)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ns + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", relationships),
                new XElement(ns + "sheets",
                    new XElement(ns + "sheet",
                        new XAttribute("name", SanitizeWorksheetName(sheetName)),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(relationships + "id", "rId1")))));
    }

    private static XDocument BuildWorkbookRelationshipsDocument()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ns + "Relationships",
                new XElement(ns + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml")),
                new XElement(ns + "Relationship",
                    new XAttribute("Id", "rId2"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml"))));
    }

    private static XDocument BuildStylesDocument()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ns + "styleSheet",
                new XElement(ns + "fonts",
                    new XAttribute("count", "1"),
                    new XElement(ns + "font",
                        new XElement(ns + "sz", new XAttribute("val", "11")),
                        new XElement(ns + "name", new XAttribute("val", "Calibri")))),
                new XElement(ns + "fills",
                    new XAttribute("count", "2"),
                    new XElement(ns + "fill",
                        new XElement(ns + "patternFill", new XAttribute("patternType", "none"))),
                    new XElement(ns + "fill",
                        new XElement(ns + "patternFill", new XAttribute("patternType", "gray125")))),
                new XElement(ns + "borders",
                    new XAttribute("count", "1"),
                    new XElement(ns + "border",
                        new XElement(ns + "left"),
                        new XElement(ns + "right"),
                        new XElement(ns + "top"),
                        new XElement(ns + "bottom"),
                        new XElement(ns + "diagonal"))),
                new XElement(ns + "cellStyleXfs",
                    new XAttribute("count", "1"),
                    new XElement(ns + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"))),
                new XElement(ns + "cellXfs",
                    new XAttribute("count", "1"),
                    new XElement(ns + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"),
                        new XAttribute("xfId", "0"))),
                new XElement(ns + "cellStyles",
                    new XAttribute("count", "1"),
                    new XElement(ns + "cellStyle",
                        new XAttribute("name", "Normal"),
                        new XAttribute("xfId", "0"),
                        new XAttribute("builtinId", "0")))));
    }

    private static XDocument BuildWorksheetDocument(TabularExportTable table)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var rows = new List<XElement>
        {
            BuildWorksheetRow(ns, 1, table.Columns.Select(column => column.Header).ToList())
        };

        for (var index = 0; index < table.Rows.Count; index++)
        {
            var values = table.Columns
                .Select(column => GetValue(table.Rows[index], column.Key) ?? string.Empty)
                .ToList();

            rows.Add(BuildWorksheetRow(ns, index + 2, values));
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(ns + "worksheet",
                new XElement(ns + "sheetData", rows)));
    }

    private static XElement BuildWorksheetRow(XNamespace ns, int rowIndex, IReadOnlyList<string> values)
        => new(
            ns + "row",
            new XAttribute("r", rowIndex),
            values.Select((value, columnIndex) => BuildWorksheetCell(ns, rowIndex, columnIndex + 1, value)));

    private static XElement BuildWorksheetCell(XNamespace ns, int rowIndex, int columnIndex, string value)
    {
        var sanitized = SanitizeXmlValue(value);
        var text = new XElement(ns + "t", sanitized);
        if (RequiresPreserveSpace(sanitized))
        {
            text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        }

        return new XElement(
            ns + "c",
            new XAttribute("r", $"{GetColumnReference(columnIndex)}{rowIndex}"),
            new XAttribute("t", "inlineStr"),
            new XElement(ns + "is", text));
    }

    private static bool RequiresPreserveSpace(string value)
        => value.Length != value.Trim().Length || value.Contains('\n') || value.Contains('\r') || value.Contains('\t');

    private static string GetColumnReference(int columnIndex)
    {
        var dividend = columnIndex;
        var builder = new StringBuilder();

        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            builder.Insert(0, (char)('A' + modulo));
            dividend = (dividend - modulo) / 26;
        }

        return builder.ToString();
    }

    private static string SanitizeWorksheetName(string value)
    {
        var invalidCharacters = new[] { '[', ']', ':', '*', '?', '/', '\\' };
        var sanitized = new string(value
            .Where(character => !invalidCharacters.Contains(character) && !char.IsControl(character))
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Export";
        }

        return sanitized.Length <= 31 ? sanitized : sanitized[..31];
    }

    private static string SanitizeXmlValue(string value)
        => new(value.Where(XmlConvert.IsXmlChar).ToArray());
}