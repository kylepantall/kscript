namespace KScript.Syntax.Markup
{
    /// <summary>
    /// The one reader of where a type's fields are written: lines of its block, and the fields its
    /// <c>construct({ ... })</c> lists. The runtime, the editor and the generators all ask here.
    /// </summary>
    public static class TypeBody
    {
        /// <summary>The type's &lt;construct&gt;, or null: <c>construct({ note: Note }) { ... }</c>.</summary>
        public static MarkupElement Constructor(MarkupElement type) =>
            type?.ChildElements.FirstOrDefault(c => c.Name == CodeParser.ConstructWord);

        /// <summary>Every &lt;field&gt; of the type in source order, the constructor's among them.</summary>
        public static IEnumerable<MarkupElement> Fields(MarkupElement type)
        {
            foreach (MarkupElement child in type?.ChildElements ?? Enumerable.Empty<MarkupElement>())
            {
                if (child.Name == "field") yield return child;
                else if (child.Name == CodeParser.ConstructWord)
                    foreach (MarkupElement input in child.ChildElements.Where(c => c.Name == "field")) yield return input;
            }
        }

        /// <summary>The word, and the XML attribute, that keeps a field or method to the type's own code.</summary>
        public const string PrivateWord = "private";

        /// <summary>True for a field or method only the type's own code can use: <c>private count: int</c>, <c>private fn tally()</c>.</summary>
        public static bool IsPrivate(MarkupElement member) =>
            member?.GetAttributeValue(PrivateWord)?.Trim().ToLowerInvariant() is "yes" or "true";

        /// <summary>What to tell code outside a type that uses one of its private members.</summary>
        public static string PrivateMessage(string member, string type) =>
            $"'{member}' is private to {type}. Only {type}'s own methods can use it.";

        /// <summary>
        /// True when a caller may pass the field: every stored field that isn't private, or with a constructor only
        /// the ones it lists.
        /// </summary>
        public static bool IsInput(MarkupElement field) =>
            field.GetAttribute("value") == null && !IsPrivate(field)
            && (field.Parent?.Name == CodeParser.ConstructWord || Constructor(field.Parent) == null);

        /// <summary>The fields a caller may pass, in source order.</summary>
        public static IEnumerable<MarkupElement> Inputs(MarkupElement type) => Fields(type).Where(IsInput);
    }
}
