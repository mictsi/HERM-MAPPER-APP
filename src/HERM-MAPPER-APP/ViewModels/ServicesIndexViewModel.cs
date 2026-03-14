using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

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
    public int ProductCount { get; init; }
    public int ConnectionCount { get; init; }
    public string ProductPreview { get; init; } = "-";
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
