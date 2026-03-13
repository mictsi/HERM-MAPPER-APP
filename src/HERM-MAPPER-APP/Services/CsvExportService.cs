using System.Text;
using HERMMapperApp.Models;

namespace HERMMapperApp.Services;

public static class CsvExportService
{
    public static string BuildProductMappingExport(IEnumerable<ProductMapping> mappings)
    {
        var builder = new StringBuilder();

        builder.AppendLine("MODEL;DOMAIN;CAPABILITY;COMPONENT;PRODUCT");

        foreach (var mapping in mappings)
        {
            var capability = mapping.TrmComponent?.ParentCapability ?? mapping.TrmCapability;
            var domain = mapping.TrmComponent?.ParentCapability?.ParentDomain ?? capability?.ParentDomain ?? mapping.TrmDomain;

            AppendRow(builder,
            [
                "HERM",
                domain is null ? null : $"{domain.Code} {domain.Name}",
                capability is null ? null : $"{capability.Code} {capability.Name}",
                mapping.TrmComponent?.DisplayLabel,
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
