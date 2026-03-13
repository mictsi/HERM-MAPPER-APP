using HERMMapperApp.Data;
using HERMMapperApp.Models;
using Microsoft.EntityFrameworkCore;

namespace HERMMapperApp.Services;

public sealed class ComponentVersioningService(AppDbContext dbContext)
{
    public async Task RecordVersionAsync(
        int componentId,
        string changeType,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var component = await dbContext.TrmComponents
            .AsNoTracking()
            .Include(x => x.CapabilityLinks)
            .ThenInclude(x => x.TrmCapability)
            .FirstOrDefaultAsync(x => x.Id == componentId, cancellationToken);

        if (component is null)
        {
            return;
        }

        var nextVersion = await dbContext.TrmComponentVersions
            .Where(x => x.TrmComponentId == componentId)
            .Select(x => (int?)x.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var capabilityLinks = component.CapabilityLinks
            .Where(x => x.TrmCapability is not null)
            .Select(x => x.TrmCapability!)
            .OrderBy(x => x.Code)
            .ToList();

        dbContext.TrmComponentVersions.Add(new TrmComponentVersion
        {
            TrmComponentId = component.Id,
            VersionNumber = nextVersion + 1,
            ChangeType = changeType,
            ModelCode = component.Code,
            TechnologyComponentCode = component.TechnologyComponentCode,
            Name = component.Name,
            IsCustom = component.IsCustom,
            IsDeleted = component.IsDeleted,
            CapabilityCodes = string.Join(", ", capabilityLinks.Select(x => x.Code)),
            CapabilityNames = string.Join(", ", capabilityLinks.Select(x => x.Name)),
            Description = component.Description,
            Comments = component.Comments,
            ProductExamples = component.ProductExamples,
            Details = details,
            ChangedUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
