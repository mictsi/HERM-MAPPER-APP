using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ComponentHistoryViewModel
{
    public TrmComponent Component { get; init; } = new();
    public IReadOnlyList<TrmComponentVersion> Versions { get; init; } = [];
}
