using KScript.Language.Analysis;
using KScript.Pages;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features;

public sealed partial class LanguageService
{
    private List<CompletionItem> ComponentPropValues(DocumentAnalysis caller, Cursor cursor)
    {
        var attribute = cursor.Attribute;
        string path = ComponentFile(caller.Path, cursor.TagElement.Name);
        if (path == null || !ScriptFiles.IsScript(path) || Files.ReadText(path) is not string source) return null;

        var component = path == caller.Path ? caller : Analyzer.AnalyzeFile(source, path, Files, Catalog);
        var declaration = ComponentScript.FindType(component.Markup, cursor.TagElement.Name);
        string propName = TemplateContract.VariableName(attribute.Name);
        if (declaration == null || !TypeBody.Inputs(declaration).Any(field => field.GetAttributeValue("name")?.TrimEnd('?') == propName))
            return null;
        var componentType = component.RootScope.Lookup(declaration.GetAttributeValue("name"), int.MaxValue)?.Shape.Instance;
        if (componentType == null || !componentType.Fields.TryGetValue(propName, out var prop) || prop.ValueType is not string type)
            return null;

        // Only complete a whole prop at its value slot. Let the expression service handle nested calls and members.
        TextSpan span = attribute.Value.SourceSpan;
        string value = caller.Source[span];
        int start = span.Start, end = span.End;
        bool expression = value.TrimStart().StartsWith('{');
        bool needsClose = false;
        bool inString = false;
        if (expression)
        {
            start += value.IndexOf('{') + 1;
            int last = value.TrimEnd().Length - 1;
            if (value[last] == '}') end = span.Start + last;
            else needsClose = true;
            if (cursor.Offset < start || cursor.Offset > end) return new();
            string body = caller.Source.Text[start..end].Trim();
            if (body.Length > 0 && body[0] is '\'' or '"')
            {
                int closeQuote = body.IndexOf(body[0], 1);
                if (closeQuote >= 0 && closeQuote != body.Length - 1) return null;
                inString = true;
            }
            else
            {
                if (body.Length > 0 && body[0] is '$' or '@') body = body[1..];
                if (body.Any(ch => !Lexer.IsIdentChar(ch) && ch != '-')) return null;
            }
        }

        var replace = TextSpan.FromBounds(start, end);
        string typed = caller.Source.Text[start..cursor.Offset].TrimStart();
        if (inString && typed.Length > 0) typed = typed[1..];
        bool finite = EnumValuesOf(component, SplitType(type).Name) != null || IsFiniteRefinement(component, type);
        bool literalText = IsTextualChoice(component, type);
        var items = new List<CompletionItem>();
        foreach (var option in AcceptedValues(component, type, typed))
        {
            if ((!literalText && inString) || !option.Value.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) continue;
            string literal = literalText ? QuotedValue(option.Value) : option.Value;
            string insert = literalText && !expression ? option.Value : AsExpression(literal);
            TextSpan edit = replace;
            if ((attribute.Quote != '\0' && insert.Contains(attribute.Quote))
                || literalText && (option.Value.IndexOfAny(['{', '}', '&', '<', '>']) >= 0
                    || !expression && attribute.Quote == '\0'))
            {
                // An unquoted {expression} is parsed by KScript. HTML quotes, entities and braces inside its
                // string cannot end the attribute or change its value. Replace any enclosing HTML quotes too.
                int from = span.Start - (attribute.Quote != '\0' ? 1 : 0);
                bool closed = attribute.Quote != '\0' && span.End < caller.Source.Text.Length && caller.Source.Text[span.End] == attribute.Quote;
                edit = TextSpan.FromBounds(from, span.End + (closed ? 1 : 0));
                insert = "{" + literal + "}";
            }
            items.Add(new CompletionItem { Label = option.Value, Kind = CompletionKind.Value, InsertText = insert,
                ReplaceSpan = edit, Detail = option.Detail ?? type, Documentation = option.Documentation, SortText = "0" + option.Value });
        }
        if (inString) return items;
        if (type.EndsWith('?') && "null".StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            items.Add(new CompletionItem { Label = "null", Kind = CompletionKind.Value, InsertText = AsExpression("null"), ReplaceSpan = replace });
        if (finite) return items;
        if (type.TrimEnd('?') is "int" or "float" or "number" && "0".StartsWith(typed, StringComparison.Ordinal))
            items.Add(new CompletionItem { Label = "0", Kind = CompletionKind.Value, Detail = type, InsertText = AsExpression("0"), ReplaceSpan = replace });
        if ((type.TrimEnd('?').EndsWith("[]") || type.TrimEnd('?') == KType.List) && typed.Length == 0)
            items.Add(new CompletionItem { Label = "[]", Kind = CompletionKind.Value, Detail = type, InsertText = AsExpression("[]"), ReplaceSpan = replace });
        if (!expression && typed.Length > 0 && typed[0] is not ('$' or '@')) return items;

        // Resolve suggestions in the calling scope. The component's enum/constraint definitions can be private to its file.
        if (new TypeInference(Catalog, caller).TypeNamed(type, cursor.Offset) is not { Type: not KType.Any }) return items;
        var variables = RankVariables(caller, cursor.Offset, VariableCompletions(caller, cursor.Offset, replace, withSigil: true), type, matchingOnly: true);
        var commands = MatchingFieldCommands(caller, cursor.Offset, type, CommandCompletions(caller, replace, withSigil: true));
        foreach (var item in variables.Concat(commands).Where(i => i.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase)))
            items.Add(new CompletionItem { Label = item.Label, FilterText = item.Label, Kind = item.Kind, Detail = item.Detail,
                Documentation = item.Documentation, InsertText = AsExpression(item.InsertText ?? item.Label), IsSnippet = item.IsSnippet,
                AdditionalEdits = item.AdditionalEdits, ReplaceSpan = replace, SortText = item.SortText, SuggestAgain = item.SuggestAgain });
        return items;

        string AsExpression(string text) => expression ? text + (needsClose ? "}" : "") : "{" + text + "}";
    }
}
