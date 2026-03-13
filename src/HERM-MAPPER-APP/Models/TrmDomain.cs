using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.Models;

public sealed class TrmDomain
{
    public int Id { get; set; }

    [Required, StringLength(16)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? SourceTitle { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Comments { get; set; }

    public ICollection<TrmCapability> Capabilities { get; set; } = new List<TrmCapability>();
}
