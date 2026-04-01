using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ServiceVisualizationViewModel
{
    public ServiceCatalogItem Service { get; init; } = new();
    public IReadOnlyList<string> ProductNames { get; init; } = [];
    public IReadOnlyList<ServiceConnectionViewModel> Connections { get; init; } = [];
    public ApplicationHierarchyNodeViewModel HierarchyRoot { get; init; } = new();
    public bool UsesGraphConnections { get; init; }
    public bool SupportsGraphLayout { get; init; }

    public bool HasVisualization => HierarchyRoot.Children.Count != 0;
}

public sealed class ServiceConnectionViewModel
{
    public int Sequence { get; init; }
    public int FromProductId { get; init; }
    public int ToProductId { get; init; }
    public string FromProductName { get; init; } = string.Empty;
    public string ToProductName { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
}
