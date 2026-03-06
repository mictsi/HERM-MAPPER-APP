using System.ComponentModel.DataAnnotations;

namespace HERM_MAPPER_APP.Models;

public sealed class ProductCatalogItem
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Product name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Vendor { get; set; }

    [StringLength(80)]
    public string? Version { get; set; }

    [StringLength(80)]
    [Display(Name = "Lifecycle status")]
    public string? LifecycleStatus { get; set; }

    [StringLength(120)]
    public string? Owner { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ProductMapping> Mappings { get; set; } = new List<ProductMapping>();
}
