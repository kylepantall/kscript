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

        /// <summary>True when a caller may pass the field: every stored field, or with a constructor only the ones it lists.</summary>
        public static bool IsInput(MarkupElement field) =>
            field.GetAttribute("value") == null
            && (field.Parent?.Name == CodeParser.ConstructWord || Constructor(field.Parent) == null);

        /// <summary>The fields a caller may pass, in source order.</summary>
        public static IEnumerable<MarkupElement> Inputs(MarkupElement type) => Fields(type).Where(IsInput);
    }
}
