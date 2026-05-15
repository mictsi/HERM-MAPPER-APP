using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HERMMapperApp.Models;

public sealed class DrmEntity
{
    public int Id { get; set; }

    [Required, StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? SourceTitle { get; set; }

    [StringLength(16)]
    public string? ParentTopicCode { get; set; }

    [StringLength(200)]
    public string? ParentTopicTypeName { get; set; }

    public int? ParentTopicId { get; set; }
    public DrmTopic? ParentTopic { get; set; }

    [StringLength(1000)]
    public string? AlternativeNames { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Comments { get; set; }

    [StringLength(400)]
    public string? TogafEnterpriseMetamodelEntity { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }

    [StringLength(400)]
    public string? DeletedReason { get; set; }

    public ICollection<DrmCommonSubClass> CommonSubClasses { get; set; } = new List<DrmCommonSubClass>();
    public ICollection<DrmModelDataEntity> ModelDataEntities { get; set; } = new List<DrmModelDataEntity>();

    [NotMapped]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";
}
