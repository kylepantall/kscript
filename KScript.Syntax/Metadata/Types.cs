using System;
using System.Collections.Generic;

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

        // KScript.Data: a model (<c>$User</c>), one of its records, a query, and a database connection.
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

        /// <summary>A &lt;type&gt; declared by a script: <c>$Repo</c>.</summary>
        public const string Type = "type";

        /// <summary>An error type: <c>NotFound</c> from <c>error NotFound { id: int }</c>, or a built-in such as Validation.</summary>
        public const string ErrorType = "error_type";

        public static readonly string[] Declarable = { "string", "int", "float", "bool", "list", "map", "fn", "email", "password", "handle", "uuid", "regex" };

        /// <summary>True if a method declared for <paramref name="receiver"/> can be called on a value of type <paramref name="actual"/>.</summary>
        public static bool Accepts(string receiver, string actual)
        {
            if (receiver == Any || actual == Any || receiver == actual)
            {
                return true;
            }

            // Values are typed on use: a string that holds a number can use number methods, and anything prints as a string.
            if (receiver == String && (actual == Number || actual == Bool || actual == File || actual == Date || actual == Email || actual == Handle || actual == Uuid || actual == Regex))
            {
                return true;
            }
            if ((receiver == Number || receiver == Bool) && actual == String)
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

        /// <summary>The string methods each kind of text with a meaning of its own keeps.</summary>
        public static IReadOnlyDictionary<string, HashSet<string>> StringMethodsOf { get; } = new Dictionary<string, HashSet<string>>
        {
            // Searching, cutting up, re-casing and encoding an address all make sense; to_date and to_number don't.
            [Email] = Methods("contains", "starts_with", "ends_with", "index_of", "length", "lower", "upper", "slice", "split",
                "replace", "matches", "truncate", "pad_left", "pad_right", "escape", "url_encode", "markdown_escape", "json"),
            // A handle is already lower case and has no spaces, so it keeps the methods for searching and showing it.
            [Handle] = Methods("contains", "starts_with", "ends_with", "length", "upper", "slice", "matches", "truncate",
                "pad_left", "pad_right", "escape", "url_encode", "markdown_escape", "json"),
            // A uuid is fixed hex: compare its prefix, take a short id, upper-case it or split its groups.
            [Uuid] = Methods("contains", "starts_with", "ends_with", "upper", "slice", "split", "pad_left", "pad_right",
                "escape", "url_encode", "markdown_escape", "json"),
            // A regex has its own replace, split and match; its pattern text can still be looked at and shown.
            [Regex] = Methods("contains", "starts_with", "ends_with", "length", "escape", "markdown_escape", "json"),
        };

        private static HashSet<string> Methods(params string[] names) => new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }
}
