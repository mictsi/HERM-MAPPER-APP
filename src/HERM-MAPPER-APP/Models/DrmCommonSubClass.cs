using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HERMMapperApp.Models;

public sealed class DrmCommonSubClass
{
    public int Id { get; set; }

    [Required, StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? SourceTitle { get; set; }

    [StringLength(32)]
    public string? ParentEntityCode { get; set; }

    public int? ParentEntityId { get; set; }
    public DrmEntity? ParentEntity { get; set; }

    [StringLength(1000)]
    public string? AlternativeNames { get; set; }

    [StringLength(4000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Comments { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedUtc { get; set; }

    [StringLength(400)]
    public string? DeletedReason { get; set; }

    public ICollection<DrmModelDataEntity> ModelDataEntities { get; set; } = new List<DrmModelDataEntity>();

    [NotMapped]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Code)
        ? Name
        : $"{Code} {Name}";
}
