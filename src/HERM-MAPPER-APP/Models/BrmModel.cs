using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.Models;

public sealed class BrmModel
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Area { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required, StringLength(80)]
    public string Status { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<BusinessCapabilityCatalogItem> Capabilities { get; set; } = new List<BusinessCapabilityCatalogItem>();
}
