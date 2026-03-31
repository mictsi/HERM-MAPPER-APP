using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.Models;

public sealed class ApplicationCatalogItem
{
    public int Id { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Application name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }

    [StringLength(400)]
    public string? DeletedReason { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ApplicationCatalogItemMapping> Mappings { get; set; } = new List<ApplicationCatalogItemMapping>();
}
