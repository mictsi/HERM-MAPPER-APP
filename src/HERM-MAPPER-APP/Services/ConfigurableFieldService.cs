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
        var configuredOptions = await GetOptionsAsync(fieldName, cancellationToken);

        var items = new List<SelectListItem>
        {
            new(defaultLabel, string.Empty, string.IsNullOrWhiteSpace(selectedValue))
        };

        foreach (var value in configuredOptions.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new SelectListItem(value, value, string.Equals(value, selectedValue, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(selectedValue) &&
            configuredOptions.All(x => !string.Equals(x.Value, selectedValue, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new SelectListItem(selectedValue, selectedValue, true));
        }

        return items;
    }

    public async Task<IReadOnlyList<ConfigurableFieldOption>> GetOptionsAsync(
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ConfigurableFieldOptions
            .AsNoTracking()
            .Where(x => x.FieldName == fieldName)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedUtc)
            .ThenBy(x => x.Value)
            .ToListAsync(cancellationToken);
    }
}
