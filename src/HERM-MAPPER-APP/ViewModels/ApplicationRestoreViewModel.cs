using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ApplicationRestoreViewModel
{
    public IReadOnlyList<ApplicationCatalogItem> Applications { get; init; } = [];
    public string? StatusMessage { get; init; }
}
