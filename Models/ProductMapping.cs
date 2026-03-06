using System.ComponentModel.DataAnnotations;

namespace HERM_MAPPER_APP.Models;

public sealed class ProductMapping
{
    public int Id { get; set; }

    public int ProductCatalogItemId { get; set; }
    public ProductCatalogItem? ProductCatalogItem { get; set; }

    public int? TrmDomainId { get; set; }
    public TrmDomain? TrmDomain { get; set; }

    public int? TrmCapabilityId { get; set; }
    public TrmCapability? TrmCapability { get; set; }

    public int? TrmComponentId { get; set; }
    public TrmComponent? TrmComponent { get; set; }

    [Display(Name = "Mapping status")]
    public MappingStatus MappingStatus { get; set; } = MappingStatus.Draft;

    [Range(1, 5)]
    [Display(Name = "Fit score")]
    public int? FitScore { get; set; }

    [StringLength(4000)]
    [Display(Name = "Mapping rationale")]
    public string? MappingRationale { get; set; }

    [Display(Name = "Last reviewed")]
    public DateTime? LastReviewedUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
