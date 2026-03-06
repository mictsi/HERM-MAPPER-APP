using HERM_MAPPER_APP.Data;
using HERM_MAPPER_APP.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HERM_MAPPER_APP.Services;

public sealed class ConfigurableFieldService(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<SelectListItem>> GetSelectListAsync(
        string fieldName,
        string? selectedValue,
        string defaultLabel = "Choose",
        CancellationToken cancellationToken = default)
    {
        var configuredValues = await dbContext.ConfigurableFieldOptions
            .AsNoTracking()
            .Where(x => x.FieldName == fieldName)
            .OrderBy(x => x.Value)
            .Select(x => x.Value)
            .ToListAsync(cancellationToken);

        var items = new List<SelectListItem>
        {
            new(defaultLabel, string.Empty, string.IsNullOrWhiteSpace(selectedValue))
        };

        foreach (var value in configuredValues.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new SelectListItem(value, value, string.Equals(value, selectedValue, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(selectedValue) &&
            configuredValues.All(x => !string.Equals(x, selectedValue, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new SelectListItem(selectedValue, selectedValue, true));
        }

        return items;
    }

    public async Task<IReadOnlyList<ConfigurableFieldOption>> GetOptionsAsync(
        string fieldName,
        CancellationToken cancellationToken = default) =>
        await dbContext.ConfigurableFieldOptions
            .AsNoTracking()
            .Where(x => x.FieldName == fieldName)
            .OrderBy(x => x.Value)
            .ToListAsync(cancellationToken);
}
