using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ComponentHistoryViewModel
{
    public TrmComponent Component { get; init; } = new();
    public IReadOnlyList<TrmComponentVersion> Versions { get; init; } = [];
}
