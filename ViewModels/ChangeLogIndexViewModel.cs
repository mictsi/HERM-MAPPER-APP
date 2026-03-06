using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ChangeLogIndexViewModel
{
    public string? Search { get; init; }
    public IReadOnlyList<AuditLogEntry> Entries { get; init; } = [];
}
