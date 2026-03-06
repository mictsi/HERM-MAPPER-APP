namespace HERM_MAPPER_APP.ViewModels;

public sealed class TrmWorkbookVerificationResult
{
    public bool IsValid => Errors.Count == 0;
    public int DomainRowCount { get; init; }
    public int CapabilityRowCount { get; init; }
    public int ComponentRowCount { get; init; }
    public int DomainsToAdd { get; init; }
    public int DomainsToUpdate { get; init; }
    public int CapabilitiesToAdd { get; init; }
    public int CapabilitiesToUpdate { get; init; }
    public int ComponentsToAdd { get; init; }
    public int ComponentsToUpdate { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
