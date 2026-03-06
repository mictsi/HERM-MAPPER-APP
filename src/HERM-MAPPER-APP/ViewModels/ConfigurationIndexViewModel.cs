using System.ComponentModel.DataAnnotations;
using HERM_MAPPER_APP.Models;

namespace HERM_MAPPER_APP.ViewModels;

public sealed class ConfigurationIndexViewModel
{
    public IReadOnlyList<ConfigurationFieldGroupViewModel> Fields { get; init; } = [];
}

public sealed class ConfigurationFieldGroupViewModel
{
    public string FieldName { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<ConfigurableFieldOption> Options { get; init; } = [];
}

public sealed class AddConfigurationOptionInputModel
{
    [Required]
    public string FieldName { get; set; } = string.Empty;

    [Required, StringLength(120)]
    [Display(Name = "Value")]
    public string Value { get; set; } = string.Empty;
}
