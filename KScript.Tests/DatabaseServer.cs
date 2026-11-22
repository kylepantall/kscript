using System.Data.Common;

using KScript.Data;

namespace KScript.Tests;

/// <summary>
/// The database server some tests run on (KSCRIPT_TEST_DATABASE_URL): emptied before each test, and made the default
/// database (DATABASE_URL) of every project with no database in its config.
/// </summary>
internal static class DatabaseServer
{
    /// <summary>Drops every table, and points DATABASE_URL at the server.</summary>
    public static void Reset(string url)
    {
        Environment.SetEnvironmentVariable("DATABASE_URL", url);
        DatabaseSettings settings = DatabaseSettings.FromOptions("main", new Dictionary<string, object> { ["url"] = url }, Path.GetTempPath());
        using DbConnection connection = settings.Dialect.Open(settings);
        var tables = new List<string>();
        using (DbCommand list = connection.CreateCommand())
        {
            list.CommandText = settings.Dialect.TablesQuery;
            using DbDataReader reader = list.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }
        bool mysql = settings.Dialect is MySqlDialect;
        foreach (string table in tables)
        {
            using DbCommand drop = connection.CreateCommand();
            drop.CommandText = (mysql ? "SET FOREIGN_KEY_CHECKS = 0; " : string.Empty) + "DROP TABLE IF EXISTS " + settings.Dialect.Quote(table) + (mysql ? string.Empty : " CASCADE");
            drop.ExecuteNonQuery();
        }
    }
}
