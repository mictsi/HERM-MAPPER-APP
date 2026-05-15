using System.ComponentModel.DataAnnotations;
using HERMMapperApp.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HERMMapperApp.ViewModels;

public sealed class DrmModelsIndexViewModel
{
    public string? StatusMessage { get; init; }
    public IReadOnlyList<DrmModelIndexRowViewModel> Models { get; init; } = [];
}

public sealed class DrmModelIndexRowViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public int DataEntityCount { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed class DrmModelEditViewModel
{
    public int? Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Area { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required, StringLength(80)]
    public string Status { get; set; } = string.Empty;

    public IReadOnlyList<string> SuggestedStatuses { get; init; } = [];
}

public sealed class DrmModelDetailsViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime UpdatedUtc { get; init; }
    public int TopicTypeCount { get; init; }
    public int TopicCount { get; init; }
    public int EntityCount { get; init; }
    public int CommonSubClassCount { get; init; }
    public string? StatusMessage { get; init; }
    public IReadOnlyList<DrmModelDataEntityRowViewModel> DataEntities { get; init; } = [];
}

public sealed class DrmModelDataEntityRowViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TopicTypeLabel { get; init; } = "-";
    public string TopicLabel { get; init; } = "-";
    public string EntityLabel { get; init; } = "-";
    public string? CommonSubClassLabel { get; init; }
    public string? Description { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed class DrmDataEntityEditViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Choose a DRM model.")]
    [Display(Name = "DRM model")]
    public int? SelectedDrmModelId { get; set; }

    public string DrmModelName { get; set; } = string.Empty;
    public string DrmModelArea { get; set; } = string.Empty;
    public string DrmModelStatus { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a DRM entity.")]
    [Display(Name = "DRM entity")]
    public int? SelectedDrmEntityId { get; set; }

    [Display(Name = "Common sub-class")]
    public int? SelectedDrmCommonSubClassId { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public IReadOnlyList<SelectListItem> EntityOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> CommonSubClassOptions { get; set; } = [];
}

public sealed class DrmDataEntityDeleteViewModel
{
    public int Id { get; init; }
    public int DrmModelId { get; init; }
    public string DrmModelName { get; init; } = "-";
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed class DrmModelRestoreViewModel
{
    public string? StatusMessage { get; init; }
    public IReadOnlyList<DrmModel> Models { get; init; } = [];
}
