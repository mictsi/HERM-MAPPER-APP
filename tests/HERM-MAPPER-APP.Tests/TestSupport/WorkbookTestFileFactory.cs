using System.IO.Compression;
using System.Xml.Linq;

namespace HERMMapperApp.Tests.TestSupport;

internal static class WorkbookTestFileFactory
{
    public static void WriteWorkbook(string path, params WorkbookSheet[] sheets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteDocument(
            archive,
            "xl/workbook.xml",
            BuildWorkbookDocument(sheets));
        WriteDocument(
            archive,
            "xl/_rels/workbook.xml.rels",
            BuildWorkbookRelationshipsDocument(sheets.Length));

        for (var index = 0; index < sheets.Length; index++)
        {
            WriteDocument(
                archive,
                $"xl/worksheets/sheet{index + 1}.xml",
                BuildWorksheetDocument(sheets[index].Rows));
        }
    }

    private static void WriteDocument(ZipArchive archive, string entryName, XDocument document)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static XDocument BuildWorkbookDocument(IReadOnlyList<WorkbookSheet> sheets)
    {
        XNamespace spreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        return new XDocument(
            new XElement(
                spreadsheetNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", relationshipNs.NamespaceName),
                new XElement(
                    spreadsheetNs + "sheets",
                    sheets.Select((sheet, index) =>
                        new XElement(
                            spreadsheetNs + "sheet",
                            new XAttribute("name", sheet.Name),
                            new XAttribute("sheetId", index + 1),
                            new XAttribute(relationshipNs + "id", $"rId{index + 1}"))))));
    }

    private static XDocument BuildWorkbookRelationshipsDocument(int sheetCount)
    {
        XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        return new XDocument(
            new XElement(
                packageRelationshipNs + "Relationships",
                Enumerable.Range(1, sheetCount).Select(index =>
                    new XElement(
                        packageRelationshipNs + "Relationship",
                        new XAttribute("Id", $"rId{index}"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", $"worksheets/sheet{index}.xml")))));
    }

    private static XDocument BuildWorksheetDocument(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        XNamespace spreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        return new XDocument(
            new XElement(
                spreadsheetNs + "worksheet",
                new XElement(
                    spreadsheetNs + "sheetData",
                    rows.Select((row, rowIndex) =>
                        new XElement(
                            spreadsheetNs + "row",
                            new XAttribute("r", rowIndex + 1),
                            row.Select((value, columnIndex) =>
                                new XElement(
                                    spreadsheetNs + "c",
                                    new XAttribute("r", $"{GetColumnReference(columnIndex + 1)}{rowIndex + 1}"),
                                    new XAttribute("t", "inlineStr"),
                                    new XElement(
                                        spreadsheetNs + "is",
                                        new XElement(spreadsheetNs + "t", value)))))))));
    }

    private static string GetColumnReference(int index)
    {
        var value = index;
        var columnName = string.Empty;

        while (value > 0)
        {
            value--;
            columnName = (char)('A' + (value % 26)) + columnName;
            value /= 26;
        }

        return columnName;
    }
}

internal sealed record WorkbookSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);