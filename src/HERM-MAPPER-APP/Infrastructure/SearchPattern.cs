namespace HERM_MAPPER_APP.Infrastructure;

internal static class SearchPattern
{
    public static string? CreateContainsPattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        return $"%{search.Trim()}%";
    }
}
