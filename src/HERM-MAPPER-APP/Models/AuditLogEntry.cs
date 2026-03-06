using System.ComponentModel.DataAnnotations;

namespace HERM_MAPPER_APP.Models;

public sealed class AuditLogEntry
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string Category { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Action { get; set; } = string.Empty;

    [StringLength(80)]
    public string? EntityType { get; set; }

    public int? EntityId { get; set; }

    [Required, StringLength(400)]
    public string Summary { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Details { get; set; }

    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}
