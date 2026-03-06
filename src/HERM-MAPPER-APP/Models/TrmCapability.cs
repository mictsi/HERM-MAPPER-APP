using System.ComponentModel.DataAnnotations;

namespace HERM_MAPPER_APP.Models;

public sealed class TrmCapability
{
    public int Id { get; set; }

    [Required, StringLength(16)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? SourceTitle { get; set; }

    [StringLength(16)]
    public string? ParentDomainCode { get; set; }

    public int? ParentDomainId { get; set; }
    public TrmDomain? ParentDomain { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Comments { get; set; }

    public ICollection<TrmComponent> Components { get; set; } = new List<TrmComponent>();
    public ICollection<TrmComponentCapabilityLink> ComponentLinks { get; set; } = new List<TrmComponentCapabilityLink>();
}
