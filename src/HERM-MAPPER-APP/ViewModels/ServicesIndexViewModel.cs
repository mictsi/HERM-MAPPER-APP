using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ServicesIndexViewModel
{
    public string? Search { get; init; }
    public string? Owner { get; init; }
    public string? LifecycleStatus { get; init; }
    public string Sort { get; init; } = ServiceSortOptions.UpdatedDesc;
    public IReadOnlyList<ConfigurableFieldOption> AvailableOwners { get; init; } = [];
    public IReadOnlyList<ConfigurableFieldOption> LifecycleStatuses { get; init; } = [];
    public IReadOnlyList<ServiceIndexRowViewModel> Services { get; init; } = [];
}

public sealed class ServiceIndexRowViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Owner { get; init; } = string.Empty;
    public string LifecycleStatus { get; init; } = string.Empty;
    public DateTime UpdatedUtc { get; init; }
    public IReadOnlyList<string> ProductNames { get; init; } = [];

    public int ProductCount => ProductNames.Count;
    public int ConnectionCount => Math.Max(0, ProductNames.Count - 1);
    public string ProductPreview => ProductNames.Count switch
    {
        0 => "-",
        <= 3 => string.Join(", ", ProductNames),
        _ => $"{string.Join(", ", ProductNames.Take(3))} +{ProductNames.Count - 3} more"
    };
}

public static class ServiceSortOptions
{
    public const string Name = "name";
    public const string NameDesc = "name_desc";
    public const string Owner = "owner";
    public const string Lifecycle = "lifecycle";
    public const string ProductCountDesc = "product_count_desc";
    public const string UpdatedAsc = "updated_asc";
    public const string UpdatedDesc = "updated_desc";
}
