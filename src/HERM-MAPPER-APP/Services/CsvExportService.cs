using System.Text;
using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.Services;

public sealed class CsvExportService
{
    public string BuildProductMappingExport(IEnumerable<ProductMapping> mappings)
    {
        var builder = new StringBuilder();

        builder.AppendLine("LEVEL0;LEVEL1;LEVEL2;LEVEL3");

        foreach (var mapping in mappings)
        {
            var componentLabel = mapping.TrmComponent is null
                ? string.Empty
                : $"{mapping.TrmComponent.Name} ({mapping.TrmComponent.DisplayCode})";

            AppendRow(builder,
            [
                "HERM",
                mapping.TrmDomain?.Name,
                componentLabel,
                mapping.ProductCatalogItem?.Name
            ]);
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<string?> values)
    {
        builder.AppendLine(string.Join(";", values.Select(Escape)));
    }

    private static string Escape(string? value)
    {
        var normalized = value ?? string.Empty;
        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }
}
