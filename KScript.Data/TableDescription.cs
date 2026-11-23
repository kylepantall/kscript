using KScript.Runtime;

namespace KScript.Data
{
    internal static class TableDescription
    {
        internal static Dictionary<string, object> Read(Database db, CallContext call, string table)
        {
            if (!db.Tables(call.Interpreter).Contains(table))
                throw call.Error($"There is no table named '{table}'.", "NotFound");

            if (db.Dialect.Name == "sqlite")
            {
                var columns = Select("""
                    SELECT name, type, "notnull" = 0 AND pk = 0 AS nullable, dflt_value AS default_value, pk AS primary_position
                    FROM pragma_table_info(@p1) ORDER BY cid
                    """);
                var indexes = Select("""
                    SELECT name, "unique" AS is_unique, origin FROM pragma_index_list(@p1) ORDER BY name
                    """);
                foreach (Dictionary<string, object> index in indexes)
                    index["columns"] = Select("SELECT name FROM pragma_index_info(@p1) ORDER BY seqno", index["name"])
                        .Cast<Dictionary<string, object>>().Select(column => column["name"]).ToList();
                var keys = Select("""
                    SELECT id, seq AS position, "from" AS column_name, "table" AS target_table, "to" AS target_column
                    FROM pragma_foreign_key_list(@p1) ORDER BY id, seq
                    """);
                return Result(columns, indexes, keys);
            }

            bool postgres = db.Dialect.Name == "postgres";
            string schema = postgres ? "current_schema()" : "database()";
            var fields = Select($"""
                SELECT column_name AS name, data_type AS type, CASE WHEN is_nullable = 'YES' THEN 1 ELSE 0 END AS nullable,
                    column_default AS default_value
                FROM information_schema.columns WHERE table_schema = {schema} AND table_name = @p1 ORDER BY ordinal_position
                """);
            var indexesByProvider = postgres
                ? Select("SELECT indexname AS name, indexdef AS definition FROM pg_indexes WHERE schemaname = current_schema() AND tablename = @p1 ORDER BY indexname")
                : Select("""
                    SELECT index_name AS name, non_unique = 0 AS is_unique, column_name, seq_in_index AS position
                    FROM information_schema.statistics WHERE table_schema = database() AND table_name = @p1 ORDER BY index_name, seq_in_index
                    """);
            var constraints = Select($"""
                SELECT c.constraint_name AS name, c.constraint_type AS kind, k.column_name, k.ordinal_position AS position
                FROM information_schema.table_constraints c
                JOIN information_schema.key_column_usage k ON k.constraint_schema = c.constraint_schema
                    AND k.table_name = c.table_name AND k.constraint_name = c.constraint_name
                WHERE c.table_schema = {schema} AND c.table_name = @p1
                ORDER BY c.constraint_name, k.ordinal_position
                """);
            return Result(fields, indexesByProvider, constraints);

            List<object> Select(string sql, object argument = null) => db.Rows(call.Interpreter,
                new SqlStatement(sql, new[] { argument ?? table }), table)
                .Select(row => (object)row.ToDictionary(entry => entry.Key, entry => Rows.Plain(entry.Value), StringComparer.Ordinal)).ToList();

            Dictionary<string, object> Result(List<object> columns, List<object> indexes, List<object> keys) => new()
            {
                ["table"] = table, ["columns"] = columns, ["indexes"] = indexes, ["keys"] = keys,
            };
        }
    }
}
