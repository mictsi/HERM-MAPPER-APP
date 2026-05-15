using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HERMMapperApp.Models;

public sealed class DrmTopic
{
    public int Id { get; set; }

    [Required, StringLength(16)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? SourceTitle { get; set; }

    [StringLength(16)]
    public string? TopicTypeCode { get; set; }

    [StringLength(200)]
    public string? TopicTypeName { get; set; }

    public int? TopicTypeId { get; set; }
    public DrmTopicType? TopicType { get; set; }

    [StringLength(1000)]
    public string? AlternativeNames { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Comments { get; set; }

    public ICollection<DrmEntity> Entities { get; set; } = new List<DrmEntity>();

    [NotMapped]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";
}
