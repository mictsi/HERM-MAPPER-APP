using System.ComponentModel.DataAnnotations;

namespace HERM_MAPPER_APP.Models;

public sealed class TrmComponentVersion
{
    public int Id { get; set; }

    public int TrmComponentId { get; set; }
    public TrmComponent? TrmComponent { get; set; }

    public int VersionNumber { get; set; }

    [Required, StringLength(40)]
    public string ChangeType { get; set; } = string.Empty;

    [StringLength(32)]
    public string? ModelCode { get; set; }

    [StringLength(32)]
    public string? TechnologyComponentCode { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsCustom { get; set; }
    public bool IsDeleted { get; set; }

    [StringLength(2000)]
    public string? CapabilityCodes { get; set; }

    [StringLength(2000)]
    public string? CapabilityNames { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Comments { get; set; }

    [StringLength(4000)]
    public string? ProductExamples { get; set; }

    [StringLength(2000)]
    public string? Details { get; set; }

    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
}
