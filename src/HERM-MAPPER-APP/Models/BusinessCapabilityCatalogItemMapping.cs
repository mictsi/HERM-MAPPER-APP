using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.Models;

public sealed class BusinessCapabilityCatalogItemMapping
{
    public int Id { get; set; }

    public int BusinessCapabilityCatalogItemId { get; set; }
    public BusinessCapabilityCatalogItem? BusinessCapabilityCatalogItem { get; set; }

    [Display(Name = "BRM capability")]
    public int BrmComponentId { get; set; }
    public BrmComponent? BrmComponent { get; set; }

    [Display(Name = "ARM component")]
    public int ArmComponentId { get; set; }
    public ArmComponent? ArmComponent { get; set; }

    public bool IsPrimary { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
