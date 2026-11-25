using KScript.Metadata;
using PackageBuilder = KScript.Runtime.KScriptPackage.PackageBuilder;
using KScript.Syntax.Text;

namespace KScript.Runtime
{
    /// <summary>Source text with the format and extension named by its fence.</summary>
    public sealed class FencedFile : IKObject
    {
        private readonly string tag;
        private readonly string text;

        internal FencedFile(string tag, string text)
        {
            this.tag = tag;
            this.text = text;
        }

        public static FencedFile Source(string text, bool latex = false) => new FencedFile(latex ? "latex" : "markdown", text);

        public string TypeName => StringBlock.ValueType(tag);

        public string Extension => StringBlock.FileExtension(tag);

        public string ToText() => text;

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "text" => text,
                "extension" => Extension,
                _ => null,
            };
            return value != null;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        internal static void Register(PackageBuilder p)
        {
            foreach (string type in new[] { KType.FormattedFile, KType.Markdown, KType.Latex })
            {
                p.Field(type, "text", KType.String, "The source text of the fenced value.");
                p.Field(type, "extension", KType.String, "The source file extension without the dot.");
            }
            RegisterMethods(p, KType.FormattedFile);
        }

        public static void RegisterDocumentMethods(PackageBuilder p)
        {
            RegisterMethods(p, KType.Markdown);
            RegisterMethods(p, KType.Latex);
        }

        private static void RegisterMethods(PackageBuilder p, string type)
        {
            p.Method(type, "find", "text: string", "The zero-based source offset of the first literal match, or -1.",
                KType.Number, (value, call) => (double)((FencedFile)value).Find(call.Str(0)));
            p.Method(type, "replace", "search: string, replacement: string",
                "Returns a copy with every literal match replaced.", type, (value, call) => ((FencedFile)value).Replace(call.Str(0), call.Str(1)));
            p.Method(type, "insert", "content, at?: int",
                "Returns a copy with text or source of the same format inserted at a zero-based offset. With no offset, appends.",
                type, (value, call) => ((FencedFile)value).Insert(call.Arg(0), call.Has(1) ? call.Num(1) : null));
            p.Method(type, "save", "path: string",
                "Writes the source to a file. Adds the fence extension when the path has no extension.", KType.Null, (value, call) =>
                {
                    ((FencedFile)value).Save(call.Host, call.Str(0), call.Module?.Directory);
                    return null;
                });
        }

        public int Find(string search) => text.IndexOf(search, StringComparison.Ordinal);

        public FencedFile Replace(string search, string replacement)
        {
            if (search.Length == 0) throw new KScriptException("The text to replace can't be empty.", "Argument");
            return new FencedFile(tag, text.Replace(search, replacement, StringComparison.Ordinal));
        }

        public FencedFile Insert(object value, double? at = null)
        {
            double offset = at ?? text.Length;
            if (!double.IsFinite(offset) || offset != Math.Truncate(offset) || offset < 0 || offset > text.Length)
                throw new KScriptException($"The insertion offset must be a whole number from 0 through {text.Length}.", "Argument");
            string content = value switch
            {
                string plain => plain,
                FencedFile file when file.Extension == Extension => file.ToText(),
                _ => throw new KScriptException($"Insert text or source with the same .{Extension} extension.", "Argument"),
            };
            return new FencedFile(tag, text.Insert((int)offset, content));
        }

        public void Save(KScriptHost host, string path, string baseDirectory)
        {
            host.CheckWrite("Saving fenced source");
            if (string.IsNullOrWhiteSpace(path))
                throw new KScriptException("Give the file a name to save it.", "Argument");
            if (Path.EndsInDirectorySeparator(path))
                throw new KScriptException("Give the file a name inside the folder.", "Argument");
            if (Path.GetExtension(path).Length == 0) path += "." + Extension;
            string full = host.ResolvePath(path, baseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
        }
    }
}
