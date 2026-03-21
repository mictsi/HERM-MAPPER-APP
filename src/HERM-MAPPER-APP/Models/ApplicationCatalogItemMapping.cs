using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.Models;

public sealed class ApplicationCatalogItemMapping
{
    public int Id { get; set; }

    public int ApplicationCatalogItemId { get; set; }
    public ApplicationCatalogItem? ApplicationCatalogItem { get; set; }

    [Display(Name = "ARM component")]
    public int ArmComponentId { get; set; }
    public ArmComponent? ArmComponent { get; set; }

    [Display(Name = "Supporting product")]
    public int ProductCatalogItemId { get; set; }
    public ProductCatalogItem? ProductCatalogItem { get; set; }

    public bool IsPrimary { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
