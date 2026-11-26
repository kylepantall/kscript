using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        /// <summary>
        /// The component a statement draws (<c>NoteCard(note: $note)</c> in a window, a document or on its own), or
        /// null when the name isn't a component declared here or in a file this one imports.
        /// </summary>
        private MarkupElement ComponentDrawnBy(MarkupElement e) =>
            Kind == DocumentKind.Script && e.Name.Length > 0 && char.IsUpper(e.Name[0])
                && TypeElementNamed(e.Name) is MarkupElement type && ComponentScript.IsComponentType(type) && !ComponentScript.IsNestedDeclaration(type)
                ? type : null;

        /// <summary>
        /// Where a statement draws: inside a window or a document, in the render it is written in, or else the
        /// terminal. Null inside a function, which draws wherever it is called from.
        /// </summary>
        private string SurfaceAt(MarkupElement e)
        {
            foreach (MarkupElement outer in e.Ancestors())
            {
                if (ComponentScript.IsRender(outer)) return ComponentScript.SurfaceOf(outer, element => catalog.GetElement(element)?.Package);
                if (outer.Name == "fn") return null;
                if (ComponentScript.SurfaceOfElement(catalog.GetElement(ElementInfo.BareName(outer.Name))?.Package) is string surface) return surface;
            }
            return ComponentScript.Terminal;
        }

        /// <summary>Checks a component written as a statement: what it is passed, and the content it is given.</summary>
        private void WalkComponentElement(MarkupElement e, MarkupElement type, Scope scope, Scope inner)
        {
            if (scope.Lookup(e.Name, e.Span.Start) is Symbol local && IsTypeSymbol(local))
                result.References.Add(new SymbolReference(local, e.NameSpan, false, false));
            else
                ReferenceImportedType(e.Name, e.NameSpan);

            string surface = SurfaceAt(e);
            if (surface != null && !type.ChildElements.Where(ComponentScript.IsRender)
                    .Any(render => ComponentScript.SurfaceOf(render, element => catalog.GetElement(element)?.Package) == surface))
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, ComponentScript.NoRenderFor(e.Name, surface));

            var inputs = TemplateContracts.Inputs(type).ToList();
            var own = TypeBody.Fields(type).Where(f => f.GetAttribute("value") == null && !TypeBody.IsInput(f))
                .Select(f => f.GetAttributeValue("name")?.Trim().TrimEnd('?')).ToHashSet(StringComparer.Ordinal);
            var passed = new HashSet<string>(StringComparer.Ordinal);
            foreach (MarkupAttribute attribute in e.Attributes)
            {
                string name = attribute.Name.Replace('-', '_');
                passed.Add(name);
                if (own.Contains(name))
                {
                    Report(attribute.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter,
                        $"'{attribute.Name}' is the component's own state and can't be passed in. Its construct block lists what it takes.");
                }
                else if (name != "id" && inputs.All(i => i.Name != name))
                {
                    string takes = inputs.Count == 0 ? $"{e.Name} takes nothing." : $"{e.Name} takes: {string.Join(", ", inputs.Select(i => i.Name))}.";
                    Report(attribute.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter, $"{e.Name} has no input '{attribute.Name}'. {takes}",
                        SuggestFix(attribute.NameSpan, name, inputs.Select(i => i.Name), n => n));
                }
                if (attribute.Value != null) AnalyzeTemplate(attribute.Value, scope, e, attribute, allowBare: false);
            }
            foreach ((string name, bool required) in inputs)
            {
                if (required && !passed.Contains(name))
                    Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, $"Pass the required '{name}' to {e.Name}.");
            }
            // What is written inside is drawn where the component's render says slot, as it is written here.
            WalkChildren(e.Children, inner, markupMode: false);
        }
    }
}
