namespace KScript.Metadata
{
    /// <summary>
    /// Marks a command, method, element, attribute or parameter as deprecated: it still works, but scripts should move
    /// off it. <c>kscript check</c> and the editor warn where it is used, and the reference shows it struck through.
    /// </summary>
    public sealed class Deprecation
    {
        public Deprecation(string message, string replacement = null)
        {
            Message = (message ?? string.Empty).Trim();
            Replacement = string.IsNullOrWhiteSpace(replacement) ? null : replacement.Trim();
        }

        /// <summary>Why it is deprecated, as a sentence, or empty.</summary>
        public string Message { get; }

        /// <summary>What to use instead, as written in a script (<c>@read_text</c>, <c>-&gt;trim</c>, <c>path</c>), or null.</summary>
        public string Replacement { get; }

        /// <summary>The replacement as a bare name (@, -&gt; and &lt; &gt; taken off), offered as a rename, or null.</summary>
        public string RenameTo
        {
            get
            {
                if (Replacement == null) return null;
                string name = Replacement.TrimStart('@', '<', '-', '>').TrimEnd('>', '/', ' ');
                if (name.EndsWith("()", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 2);
                return name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') ? name : null;
            }
        }

        /// <summary>"is deprecated. {Message} Use {Replacement} instead." without the subject.</summary>
        public string Describe(string subject)
        {
            string text = subject + " is deprecated.";
            if (Message.Length > 0) text += " " + (Message.EndsWith(".", StringComparison.Ordinal) ? Message : Message + ".");
            if (Replacement != null) text += $" Use {Replacement} instead.";
            return text;
        }
    }
}
