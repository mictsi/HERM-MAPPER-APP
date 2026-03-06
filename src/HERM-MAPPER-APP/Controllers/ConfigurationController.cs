using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using HERM_MAPPER_APP.Services;
using HERM_MAPPER_APP.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Controllers;

public sealed class ConfigurationController(
    AppDbContext dbContext,
    ConfigurableFieldService configurableFieldService,
    AuditLogService auditLogService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var fields = new List<ConfigurationFieldGroupViewModel>();

        foreach (var field in ConfigurableFieldNames.All)
        {
            fields.Add(new ConfigurationFieldGroupViewModel
            {
                FieldName = field.Key,
                Label = field.Value,
                Options = await configurableFieldService.GetOptionsAsync(field.Key)
            });
        }

        return View(new ConfigurationIndexViewModel
        {
            Fields = fields
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddOption(AddConfigurationOptionInputModel input)
    {
        input.FieldName = input.FieldName?.Trim() ?? string.Empty;
        input.Value = input.Value?.Trim() ?? string.Empty;

        if (!ConfigurableFieldNames.IsSupported(input.FieldName))
        {
            TempData["ConfigurationError"] = "That field is not supported.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(input.Value))
        {
            TempData["ConfigurationError"] = "Enter a value before saving.";
            return RedirectToAction(nameof(Index));
        }

        var exists = await dbContext.ConfigurableFieldOptions.AnyAsync(x =>
            x.FieldName == input.FieldName &&
            x.Value.ToLower() == input.Value.ToLower());

        if (exists)
        {
            TempData["ConfigurationError"] = $"{ConfigurableFieldNames.GetLabel(input.FieldName)} value '{input.Value}' already exists.";
            return RedirectToAction(nameof(Index));
        }

        var option = new ConfigurableFieldOption
        {
            FieldName = input.FieldName,
            Value = input.Value,
            CreatedUtc = DateTime.UtcNow
        };

        dbContext.ConfigurableFieldOptions.Add(option);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Configuration",
            "Create",
            nameof(ConfigurableFieldOption),
            option.Id,
            $"Added configuration value '{option.Value}' to {option.FieldName}.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOption(int id)
    {
        var option = await dbContext.ConfigurableFieldOptions.FindAsync(id);
        if (option is null)
        {
            return RedirectToAction(nameof(Index));
        }

        dbContext.ConfigurableFieldOptions.Remove(option);
        await dbContext.SaveChangesAsync();
        await auditLogService.WriteAsync(
            "Configuration",
            "Delete",
            nameof(ConfigurableFieldOption),
            option.Id,
            $"Removed configuration value '{option.Value}' from {option.FieldName}.");

        return RedirectToAction(nameof(Index));
    }
}
