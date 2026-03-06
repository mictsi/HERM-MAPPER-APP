using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.Services;

public sealed class AuditLogService(AppDbContext dbContext)
{
    public async Task WriteAsync(
        string category,
        string action,
        string? entityType,
        int? entityId,
        string summary,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            Category = category,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            Details = details,
            OccurredUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
