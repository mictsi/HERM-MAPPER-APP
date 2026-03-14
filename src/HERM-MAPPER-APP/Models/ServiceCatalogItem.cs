using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HERMMapperApp.Models;

public sealed class ServiceCatalogItem
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Service name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required, StringLength(120)]
    public string Owner { get; set; } = string.Empty;

    [Required, StringLength(80)]
    [Display(Name = "Lifecycle status")]
    public string LifecycleStatus { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }

    [StringLength(400)]
    public string? DeletedReason { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ServiceCatalogItemProduct> ProductLinks { get; set; } = new List<ServiceCatalogItemProduct>();

    public List<ServiceCatalogItemProduct> GetOrderedProductLinks() => ProductLinks
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.Id)
        .ToList();

    [NotMapped]
    public int ConnectionCount => Math.Max(0, ProductLinks.Count - 1);
}
