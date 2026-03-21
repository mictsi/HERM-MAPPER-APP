using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.Models;

public sealed class BusinessCapabilityCatalogItem
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Capability name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<BusinessCapabilityCatalogItemMapping> Mappings { get; set; } = new List<BusinessCapabilityCatalogItemMapping>();
}
