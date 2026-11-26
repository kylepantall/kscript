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
        BindDirective.Reading binding = BindDirective.Read(value.Value);
        foreach ((TextSpan problemSpan, string message) in binding.Problems)
        {
            if (binding.OldPathSpan is TextSpan oldPath && oldPath.Start == problemSpan.Start && oldPath.Length == problemSpan.Length)
            {
                // @bind('draft') and bind="draft" name the field with $ now.
                TextSpan old = value.ToSource(problemSpan);
                // @bind('draft'): the value is the text inside the quotes, so the fix takes the quotes too.
                string source = result.Source.Text;
                if (old.Start > 1 && old.End < source.Length && source[old.Start - 1] is '\'' or '"' && source[old.End] == source[old.Start - 1]
                    && source[old.Start - 2] == '(')
                    old = TextSpan.FromBounds(old.Start - 1, old.End + 1);
                Report(old, DiagnosticSeverity.Error, DiagnosticCodes.OneSpelling, message,
                    new[] { new QuickFix("Change to $" + binding.Path, new TextChange(old, "$" + binding.Path)) });
                continue;
            }
            Report(value.ToSource(problemSpan), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, message);
        }
        if (binding.Get("debounce") is ExprNode debounce)
            AnalyzeExpressionRegion(Slice(value, debounce.Span), RegionKind.Expression, scope, element, attribute);
        string path = binding.Path;
        string[] names = path.Split('.');
        int index = binding.PathSpan.Start;
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
