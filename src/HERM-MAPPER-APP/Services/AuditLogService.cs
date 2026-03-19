using System.Security.Claims;
using HERMMapperApp.Data;
using HERMMapperApp.Models;
using Microsoft.AspNetCore.Http;

namespace HERMMapperApp.Services;

public sealed class AuditLogService(AppDbContext dbContext, IHttpContextAccessor? httpContextAccessor = null)
{
    public Task WriteAsync(
        string category,
        string action,
        string? entityType,
        int? entityId,
        string summary,
        string? details = null,
        CancellationToken cancellationToken = default) =>
        WriteEntryAsync(category, action, entityType, entityId, summary, details, actorUserName: null, cancellationToken);

    public Task WriteAsActorAsync(
        string category,
        string action,
        string? entityType,
        int? entityId,
        string summary,
        string? actorUserName,
        string? details = null,
        CancellationToken cancellationToken = default) =>
        WriteEntryAsync(category, action, entityType, entityId, summary, details, actorUserName, cancellationToken);

    private async Task WriteEntryAsync(
        string category,
        string action,
        string? entityType,
        int? entityId,
        string summary,
        string? details,
        string? actorUserName,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            Category = category,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ActorUserName = ResolveActorUserName(actorUserName),
            Summary = summary,
            Details = details,
            OccurredUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string? ResolveActorUserName(string? actorUserName)
    {
        if (!string.IsNullOrWhiteSpace(actorUserName))
        {
            return actorUserName.Trim();
        }

        var user = httpContextAccessor?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var resolvedUserName =
            user.Identity?.Name ??
            user.FindFirstValue(ClaimTypes.Email) ??
            user.FindFirstValue(ClaimTypes.NameIdentifier);

        return string.IsNullOrWhiteSpace(resolvedUserName)
            ? null
            : resolvedUserName.Trim();
    }
}
