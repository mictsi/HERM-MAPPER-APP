using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ServiceVisualizationViewModel
{
    public ServiceCatalogItem Service { get; init; } = new();
    public IReadOnlyList<string> ProductNames { get; init; } = [];
    public IReadOnlyList<ServiceConnectionViewModel> Connections { get; init; } = [];
}

public sealed class ServiceConnectionViewModel
{
    public int Sequence { get; init; }
    public string FromProductName { get; init; } = string.Empty;
    public string ToProductName { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
}
