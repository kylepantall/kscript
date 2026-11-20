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

        /// <summary>A &lt;type&gt; declared by a script: <c>$Repo</c>.</summary>
        public const string Type = "type";

        public static readonly string[] Declarable = { "string", "int", "float", "bool", "list", "map", "fn" };

        /// <summary>True if a method declared for <paramref name="receiver"/> can be called on a value of type <paramref name="actual"/>.</summary>
        public static bool Accepts(string receiver, string actual)
        {
            if (receiver == Any || actual == Any || receiver == actual)
            {
                return true;
            }

            // Values are typed on use: a string that holds a number can use number methods, and anything prints as a string.
            if (receiver == String && (actual == Number || actual == Bool || actual == File || actual == Date || actual == Email || actual == Handle || actual == Uuid))
            {
                return true;
            }
            if ((receiver == Number || receiver == Bool) && actual == String)
            {
                return true;
            }
            return false;
        }
    }
}
