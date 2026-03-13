using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HERMMapperApp.Infrastructure;

public static class AppDatabaseCollations
{
    public const string SqliteCaseInsensitive = "NOCASE";
    public const string SqlServerCaseInsensitive = "Latin1_General_100_CI_AS";

    public static string GetCaseInsensitive(DatabaseFacade database) =>
        database.IsSqlite()
            ? SqliteCaseInsensitive
            : SqlServerCaseInsensitive;
}