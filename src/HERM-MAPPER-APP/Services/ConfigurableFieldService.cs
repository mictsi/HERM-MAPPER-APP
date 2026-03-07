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
            .Select(x => x.Value)
            .ToListAsync(cancellationToken);
        var orderedValues = ConfigurableFieldNames.OrderValues(fieldName, configuredValues);

        var items = new List<SelectListItem>
        {
            new(defaultLabel, string.Empty, string.IsNullOrWhiteSpace(selectedValue))
        };

        foreach (var value in orderedValues.Distinct(StringComparer.OrdinalIgnoreCase))
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
        CancellationToken cancellationToken = default)
    {
        var options = await dbContext.ConfigurableFieldOptions
            .AsNoTracking()
            .Where(x => x.FieldName == fieldName)
            .ToListAsync(cancellationToken);

        var orderedValues = ConfigurableFieldNames.OrderValues(fieldName, options.Select(x => x.Value));
        var orderLookup = orderedValues
            .Select((value, index) => new { value, index })
            .ToDictionary(x => x.value, x => x.index, StringComparer.OrdinalIgnoreCase);

        return options
            .OrderBy(x => orderLookup.TryGetValue(x.Value, out var index) ? index : int.MaxValue)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
