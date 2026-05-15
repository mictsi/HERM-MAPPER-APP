using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.Models;

public sealed class DrmModelDataEntity
{
    public int Id { get; set; }

    public int DrmModelId { get; set; }
    public DrmModel? DrmModel { get; set; }

    public int DrmEntityId { get; set; }
    public DrmEntity? DrmEntity { get; set; }

    public int? DrmCommonSubClassId { get; set; }
    public DrmCommonSubClass? DrmCommonSubClass { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
