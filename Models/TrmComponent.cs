using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HERM_MAPPER_APP.Models;

public sealed class TrmComponent
{
    public int Id { get; set; }

    [Required, StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [StringLength(32)]
    public string? TechnologyComponentCode { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool IsCustom { get; set; }
    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }

    [StringLength(400)]
    public string? DeletedReason { get; set; }

    [StringLength(200)]
    public string? SourceTitle { get; set; }

    [StringLength(16)]
    public string? ParentCapabilityCode { get; set; }

    public int? ParentCapabilityId { get; set; }
    public TrmCapability? ParentCapability { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Comments { get; set; }

    [StringLength(4000)]
    public string? ProductExamples { get; set; }

    public ICollection<TrmComponentCapabilityLink> CapabilityLinks { get; set; } = new List<TrmComponentCapabilityLink>();
    public ICollection<TrmComponentVersion> Versions { get; set; } = new List<TrmComponentVersion>();

    [NotMapped]
    public string DisplayCode => IsCustom && !string.IsNullOrWhiteSpace(TechnologyComponentCode)
        ? TechnologyComponentCode
        : Code;

    [NotMapped]
    public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayCode)
        ? Name
        : $"{DisplayCode} {Name}";
}
