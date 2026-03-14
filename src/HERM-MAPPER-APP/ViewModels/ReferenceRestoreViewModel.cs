using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class ReferenceRestoreViewModel
{
    public IReadOnlyList<TrmComponent> Components { get; init; } = [];
    public string? StatusMessage { get; init; }
}
