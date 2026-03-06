using System.ComponentModel.DataAnnotations;

namespace HERM_MAPPER_APP.Models;

public sealed class TrmComponent
{
    public int Id { get; set; }

    [Required, StringLength(16)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

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
}
