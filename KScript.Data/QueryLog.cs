using System.Runtime.CompilerServices;

using KScript.Runtime;

namespace KScript.Data
{
    /// <summary>
    /// The statements a run has sent, newest last, for <c>@queries()</c>: the last <see cref="Limit"/> of them. In a
    /// test it is the test's own statements, so a test can check how many queries a page makes.
    /// </summary>
    internal sealed class QueryLog
    {
        public const int Limit = 1000;

        private static readonly ConditionalWeakTable<Interpreter, QueryLog> Logs = new ConditionalWeakTable<Interpreter, QueryLog>();

        private readonly Queue<Dictionary<string, object>> entries = new Queue<Dictionary<string, object>>();

        /// <summary>True once the running test has started its own list.</summary>
        private bool inTest;

        public static void Note(Interpreter interpreter, Database database, SqlStatement statement)
        {
            if (interpreter == null) return;
            QueryLog log = For(interpreter);
            lock (log.entries)
            {
                if (log.entries.Count == Limit) log.entries.Dequeue();
                log.entries.Enqueue(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["sql"] = statement.Text,
                    ["bindings"] = statement.Parameters.Select(Shown).ToList(),
                    ["database"] = database.Name,
                });
            }
        }

        /// <summary>A parameter as a script sees it: numbers as numbers, dates and blobs as text.</summary>
        internal static object Shown(object value) => value switch
        {
            null => null,
            string or bool or double => value,
            long or int or short or byte or decimal or float => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };

        /// <summary>The statements so far, oldest first.</summary>
        public static List<object> Of(Interpreter interpreter)
        {
            QueryLog log = For(interpreter);
            lock (log.entries) return log.entries.Select(e => (object)new Dictionary<string, object>(e, StringComparer.Ordinal)).ToList();
        }

        private static QueryLog For(Interpreter interpreter)
        {
            QueryLog log = Logs.GetValue(interpreter, _ => new QueryLog());
            KScriptHost host = interpreter.Host;
            if (host.TestRunning && !log.inTest)
            {
                // A test starts with an empty list, and ends its list when it ends.
                lock (log.entries)
                {
                    log.entries.Clear();
                    log.inTest = true;
                }
                host.AfterTest.Add(() =>
                {
                    lock (log.entries)
                    {
                        log.entries.Clear();
                        log.inTest = false;
                    }
                });
            }
            return log;
        }
    }
}
