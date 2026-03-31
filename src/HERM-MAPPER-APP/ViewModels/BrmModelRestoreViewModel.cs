using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class BrmModelRestoreViewModel
{
    public IReadOnlyList<BrmModel> Models { get; init; } = [];
    public string? StatusMessage { get; init; }
}
