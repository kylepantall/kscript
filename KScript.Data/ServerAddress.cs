using System.Globalization;

using KScript.Runtime;

namespace KScript.Data
{
    /// <summary>
    /// Where a database server is: from a url, <c>postgres://user:password@host:5432/app?sslmode=require</c>, or from
    /// host, port, database, username and password in the connection's config. Options after ? (or under options:)
    /// go to the driver as they are.
    /// </summary>
    internal sealed class ServerAddress
    {
        public string Host { get; private init; } = "localhost";

        public int Port { get; private init; }

        public string Database { get; private init; }

        public string User { get; private init; }

        public string Password { get; private init; }

        public Dictionary<string, string> Options { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The part of a url after <c>postgres://</c>: <c>user:password@host:port/database?options</c>.</summary>
        public static ServerAddress Parse(string name, string rest, int defaultPort)
        {
            string text = rest.Trim();
            string query = null;
            int q = text.IndexOf('?');
            if (q >= 0)
            {
                query = text.Substring(q + 1);
                text = text.Substring(0, q);
            }
            string database = null;
            int slash = text.IndexOf('/');
            if (slash >= 0)
            {
                database = Decode(text.Substring(slash + 1));
                text = text.Substring(0, slash);
            }
            string user = null;
            string password = null;
            int at = text.LastIndexOf('@');
            if (at >= 0)
            {
                string login = text.Substring(0, at);
                text = text.Substring(at + 1);
                int colon = login.IndexOf(':');
                user = Decode(colon < 0 ? login : login.Substring(0, colon));
                if (colon >= 0) password = Decode(login.Substring(colon + 1));
            }
            string host = text;
            int port = defaultPort;
            int portColon = text.StartsWith('[') ? text.IndexOf("]:", StringComparison.Ordinal) + 1 : text.LastIndexOf(':');
            if (portColon > 0)
            {
                string digits = text.Substring(portColon + 1);
                if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
                    throw new KScriptException($"The url of the '{name}' database connection has '{digits}' as its port: use a number such as {defaultPort}.", "Config");
                host = text.Substring(0, portColon);
            }
            host = host.Trim('[', ']');
            var address = new ServerAddress
            {
                Host = host.Length > 0 ? Decode(host) : "localhost",
                Port = port,
                Database = string.IsNullOrEmpty(database) ? null : database,
                User = string.IsNullOrEmpty(user) ? null : user,
                Password = password,
            };
            if (query != null)
            {
                foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    string key = Decode(eq < 0 ? pair : pair.Substring(0, eq));
                    address.Options[key] = eq < 0 ? "true" : Decode(pair.Substring(eq + 1));
                }
            }
            return address;
        }

        /// <summary>The address in a connection's config: host, port, database, username (or user) and password.</summary>
        public static ServerAddress From(string name, Dictionary<string, object> options, int defaultPort)
        {
            string Text(string key) => options.GetValueOrDefault(key) is object v && v != null && Values.ToStr(v).Trim().Length > 0 ? Values.ToStr(v).Trim() : null;
            int port = defaultPort;
            if (Text("port") is string p && (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535))
                throw new KScriptException($"The '{name}' database connection has '{p}' as its port: use a number such as {defaultPort}.", "Config");
            var address = new ServerAddress
            {
                Host = Text("host") ?? "localhost",
                Port = port,
                Database = Text("database"),
                User = Text("username") ?? Text("user"),
                // A password is kept as it is: spaces at its ends are part of it.
                Password = options.GetValueOrDefault("password") is object pw && pw != null ? Values.ToStr(pw) : null,
            };
            if (options.GetValueOrDefault("options") is Dictionary<string, object> extra)
            {
                foreach (KeyValuePair<string, object> kv in extra) address.Options[kv.Key] = Values.ToStr(kv.Value);
            }
            return address;
        }

        private static string Decode(string text) => Uri.UnescapeDataString(text);
    }
}
