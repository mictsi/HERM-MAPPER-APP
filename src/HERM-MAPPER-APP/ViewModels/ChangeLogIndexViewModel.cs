using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ChangeLogIndexViewModel
{
    public string? Search { get; init; }
    public IReadOnlyList<AuditLogEntry> Entries { get; init; } = [];
}
