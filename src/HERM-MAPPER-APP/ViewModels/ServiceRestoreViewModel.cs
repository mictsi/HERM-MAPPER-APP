using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ServiceRestoreViewModel
{
    public IReadOnlyList<ServiceCatalogItem> Services { get; init; } = [];
    public string? StatusMessage { get; init; }
}
