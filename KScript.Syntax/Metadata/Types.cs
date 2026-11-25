using System;
using System.Collections.Generic;
using System.Linq;

namespace KScript.Metadata
{
    /// <summary>The value types the analyzer can tell apart. Values of unknown type are <see cref="Any"/>.</summary>
    public static class KType
    {
        public const string Any = "any";
        public const string String = "string";
        public const string Number = "number";
        public const string Bool = "bool";
        public const string List = "list";
        public const string Map = "map";
        public const string Fn = "fn";
        public const string Null = "null";

        // Values returned by particular commands.
        public const string File = "file";
        public const string Date = "date";

        // Text with a meaning of its own: checked and normalised when it is converted.
        public const string Email = "email";
        public const string Password = "password";
        public const string Handle = "handle";
        public const string Uuid = "uuid";
        public const string Regex = "regex";

        public const string Response = "response";
        public const string LoopbackJsonConnection = "loopback_json_connection";
        public const string LoopbackJsonStream = "loopback_json_stream";
        public const string Chat = "chat";
        public const string Config = "config";
        public const string Logger = "logger";
        public const string Element = "element";
        public const string Error = "error";
        public const string Request = "request";
        public const string HttpResponse = "http_response";
        public const string Storage = "storage";
        public const string Cache = "cache";
        public const string Session = "session";
        public const string Users = "users";
        public const string Document = "document";
        public const string Markdown = "markdown";
        public const string Latex = "latex";
        public const string FormattedFile = "formatted_file";

        // KScript.Data: a model (<c>User</c>), one of its records, a query, and a database connection.
        public const string Model = "model";
        public const string Record = "record";
        public const string Query = "query";
        public const string Database = "database";
        public const string PasswordHash = "password_hash";

        // KScript.Queue: a job (SendWelcome(...)), its type, a chain of jobs, a queue and a fake queue for tests.
        public const string Job = "job";
        public const string JobType = "job_type";
        public const string Chain = "chain";
        public const string Queue = "queue";
        public const string FakeQueue = "fake_queue";

        /// <summary>A &lt;type&gt; declared by a script: <c>Repo</c>.</summary>
        public const string Type = "type";

        /// <summary>An error type: <c>NotFound</c> from <c>error NotFound { id: int }</c>, or a built-in such as Validation.</summary>
        public const string ErrorType = "error_type";

        /// <summary>The types a parameter or variable can declare: the basic ones, then every native type.</summary>
        public static IReadOnlyList<string> Declarable => new[] { "string", "int", "float", "bool", "list", "map", "fn", Markdown, Latex, FormattedFile, Document }.Concat(NativeTypes.Names).ToList();

        /// <summary>True for text with a meaning of its own (email, url, country, ...), which keeps some string methods.</summary>
        public static bool IsNativeText(string type) => NativeTypes.Get(type) is NativeTypeInfo info && info.StringMethods.Count > 0;

        /// <summary>True if a method declared for <paramref name="receiver"/> can be called on a value of type <paramref name="actual"/>.</summary>
        public static bool Accepts(string receiver, string actual)
        {
            if (receiver == Any || actual == Any || receiver == actual)
            {
                return true;
            }

            // Values are typed on use: a string that holds a number can use number methods. Text with a meaning of its own
            // (an email, a handle, a uuid, a regex) is still text, so it keeps some string methods (see StringMethodsOf).
            // A number, bool, date or file isn't text and has no string methods, as at run time: use ->to_string() first.
            if (receiver == String && IsNativeText(actual))
            {
                return true;
            }
            if (receiver == Number && actual == String)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// True if <c>value-&gt;name()</c>, a method declared for <paramref name="receiver"/>, can be called on a value of
        /// type <paramref name="actual"/>. Like <see cref="Accepts"/>, except that text with a meaning of its own (an
        /// email, a handle, a uuid, a regex) only keeps the string methods that make sense for it: a uuid has no
        /// <c>-&gt;to_date()</c>. Use <c>-&gt;to_string()</c> first for the rest.
        /// </summary>
        public static bool AcceptsMethod(string receiver, string name, string actual)
        {
            if (!Accepts(receiver, actual))
            {
                return false;
            }
            if (receiver == String && StringMethodsOf.TryGetValue(actual, out HashSet<string> kept))
            {
                return name != null && kept.Contains(name);
            }
            return true;
        }

        /// <summary>The string methods each kind of text with a meaning of its own keeps (see <see cref="NativeTypes"/>).</summary>
        public static IReadOnlyDictionary<string, HashSet<string>> StringMethodsOf =>
            NativeTypes.All.Where(t => t.StringMethods.Count > 0).ToDictionary(t => t.Name, t => t.StringMethods);

        private static HashSet<string> Methods(params string[] names) => new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }
}
