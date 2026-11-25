using KScript.Pages;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis;

internal static class ComponentBindings
{
    internal static IEnumerable<Symbol> Writable(DocumentAnalysis analysis, Scope scope, MarkupElement element, int offset)
    {
        var owner = element.Ancestors().FirstOrDefault(a => a.Name.Equals("live", StringComparison.OrdinalIgnoreCase));
        var page = analysis.MarkupAt(offset);
        bool classRoot = page != analysis.Markup && (owner == null || page.Children.All(n =>
            n == owner || n is MarkupText { IsWhitespace: true }));
        return scope.VisibleAt(offset).Where(s =>
        {
            var declaration = s.DeclaringElement;
            if (declaration?.Name == "state") return declaration.Parent == owner &&
                !TemplateContract.IsRequired(declaration.GetAttribute("locked") != null, declaration.GetAttributeValue("locked"));
            if (!classRoot || !ComponentScript.IsComponentType(declaration) || s.Name == "this") return false;
            if (!analysis.DeclaredTypes.TryGetValue(declaration, out var instance) || !instance.Fields.ContainsKey(s.Name)) return false;
            return !TypeBody.Fields(declaration).Any(field => field.GetAttribute("value") != null &&
                field.GetAttributeValue("name")?.TrimEnd('?') == s.Name);
        });
    }
}

public sealed partial class Analyzer
{
    private void CheckBinding(MarkupElement element, MarkupAttribute attribute, Scope scope)
    {
        var value = attribute.Value;
        string path = value.Value.Trim().TrimStart('$');
        string[] names = path.Split('.');
        int index = value.Value.IndexOf(path, StringComparison.Ordinal);
        var span = value.ToSource(new TextSpan(index, names[0].Length));
        var writable = ComponentBindings.Writable(result, scope, element, span.Start);
        var bound = writable.FirstOrDefault(s => s.Name == names[0]);
        if (bound == null)
        {
            string suggestion = Suggestions.Closest(names[0], writable.Select(s => s.Name));
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.SetUndeclared,
                $"'{names[0]}' is not a writable field of this live component." + (suggestion == null ? "" : $" Did you mean '{suggestion}'?"),
                SuggestFix(span, names[0], writable.Select(s => s.Name), s => s));
            return;
        }
        result.References.Add(new SymbolReference(bound, span, false, true));
        boundVariables.Add(bound);
        TypeShape shape = bound.Shape;
        for (int i = 1; i < names.Length; i++)
        {
            index += names[i - 1].Length + 1;
            if (shape.Fields.TryGetValue(names[i], out var field))
            {
                shape = field.Shape;
                continue;
            }
            if (shape.Closed || !shape.IsVague && shape.Type != KType.Map)
            {
                span = value.ToSource(new TextSpan(index, names[i].Length));
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownField,
                    $"'{names[i]}' is not a field of {shape.Display()}.", SuggestFix(span, names[i], shape.Fields.Keys, s => s));
            }
            return;
        }
    }
}
