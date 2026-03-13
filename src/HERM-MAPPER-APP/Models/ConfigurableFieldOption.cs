using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.Models;

public sealed class ConfigurableFieldOption
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string FieldName { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Value { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public static class ConfigurableFieldNames
{
    public const string Owner = "Owner";
    public const string LifecycleStatus = "LifecycleStatus";
    private static readonly string[] LifecycleStatusOrder =
    [
        "Propose",
        "Development",
        "Trial",
        "Production",
        "Under review",
        "Appointed",
        "Deprecate",
        "Sunset"
    ];

    private static readonly Dictionary<string, string> SupportedFields =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Owner] = "Owner",
            [LifecycleStatus] = "Lifecycle status"
        };

    public static IReadOnlyDictionary<string, string> All => SupportedFields;

    public static bool IsSupported(string? fieldName) =>
        !string.IsNullOrWhiteSpace(fieldName) && SupportedFields.ContainsKey(fieldName);

    public static string GetLabel(string fieldName) =>
        SupportedFields.TryGetValue(fieldName, out var label) ? label : fieldName;

    public static IReadOnlyList<string> GetDefaultValues(string fieldName) =>
        fieldName.Equals(LifecycleStatus, StringComparison.OrdinalIgnoreCase)
            ? LifecycleStatusOrder
            : [];

}
