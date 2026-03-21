using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HERMMapperApp.Models;

public sealed class BrmComponent
{
    public int Id { get; set; }

    [Required, StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? SourceTitle { get; set; }

    [StringLength(16)]
    public string? ParentCapabilityCode { get; set; }

    public int? ParentCapabilityId { get; set; }
    public BrmCapability? ParentCapability { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Comments { get; set; }

    [StringLength(4000)]
    public string? ProductExamples { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }

    [StringLength(400)]
    public string? DeletedReason { get; set; }

    [NotMapped]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";
}
