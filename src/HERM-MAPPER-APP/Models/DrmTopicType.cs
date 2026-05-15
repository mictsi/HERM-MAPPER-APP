using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HERMMapperApp.Models;

public sealed class DrmTopicType
{
    public int Id { get; set; }

    [Required, StringLength(16)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    public ICollection<DrmTopic> Topics { get; set; } = new List<DrmTopic>();

    [NotMapped]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";
}
