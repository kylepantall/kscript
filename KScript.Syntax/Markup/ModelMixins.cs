namespace KScript.Syntax.Markup
{
    /// <summary>
    /// Model behaviours that are mixins KScript.Data ships (<c>model Post extends Timestamps, SoftDeletes</c>). They
    /// were once options in a model's brackets (<c>timestamps</c>, <c>soft_deletes</c>); those spellings are retired
    /// outside the package's own scripts, where the mixins are declared with them. The checker and the interpreter
    /// read this one list, so they say the same thing.
    /// </summary>
    public static class ModelMixins
    {
        /// <summary>The retired option, and the mixin that replaced it.</summary>
        public static IReadOnlyDictionary<string, string> ForOption { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["timestamps"] = "Timestamps",
            ["soft_deletes"] = "SoftDeletes",
        };

        /// <summary>True for an option that is now a mixin.</summary>
        public static bool IsRetiredOption(string name) => name != null && ForOption.ContainsKey(name);

        /// <summary>What a script that uses the old option is told.</summary>
        public static string Message(string option, string model)
        {
            string mixin = ForOption[option];
            return $"{option} is now the {mixin} mixin from KScript.Data: model {model ?? "Post"} extends {mixin} {{ ... }}.";
        }
    }
}
