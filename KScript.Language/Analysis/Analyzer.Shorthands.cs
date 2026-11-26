using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        /// <summary>
        /// Checks a handler written as shorthands (<c>toggle: $open, add: $draft, to: $tags</c>): each name, each
        /// companion, the type of the field each action changes, and the statement each action stands for.
        /// </summary>
        private void AnalyzeShorthands(MarkupAttribute attr, MarkupElement e, Scope scope)
        {
            DecodedText text = attr.Value;
            HandlerShorthands.Reading reading = HandlerShorthands.Read(text.Value);
            foreach (SyntaxError error in reading.Parse.Errors)
            {
                Report(text.ToSource(error.Span), DiagnosticSeverity.Error, error.Code ?? DiagnosticCodes.StatementSyntax, error.Message);
            }
            foreach ((TextSpan span, string message, string fix) in reading.Problems)
            {
                TextSpan at = text.ToSource(span);
                Report(at, DiagnosticSeverity.Error, DiagnosticCodes.StatementSyntax, message,
                    fix != null ? new[] { new QuickFix($"Change to {fix}", new TextChange(at, fix)) } : null);
            }
            if (reading.Parse.Errors.Count > 0) return;

            foreach ((HandlerShorthands.Modifier modifier, ArgumentNode argument) in reading.Modifiers)
            {
                if (modifier.Value is HandlerShorthands.ModifierValue.Text or HandlerShorthands.ModifierValue.Duration)
                    AnalyzeExpressionRegion(Slice(text, argument.Value.Span), RegionKind.Expression, scope, e, attr);
            }
            foreach (HandlerShorthands.Step step in reading.Steps)
            {
                if (step.Action.Name is HandlerShorthands.Reset or HandlerShorthands.Cycle)
                {
                    // The field (or list of fields) must exist; cycle:'s through: is the list it goes round.
                    AnalyzeExpressionRegion(Slice(text, step.Value.Value.Span), RegionKind.Expression, scope, e, attr);
                    if (step.Companion != null)
                    {
                        ExpressionRegion through = AnalyzeExpressionRegion(Slice(text, step.Companion.Value.Span), RegionKind.Expression, scope, e, attr);
                        string listType = through.Parse.Success && through.Parse.Root is ExprNode root ? Infer(root, through).Type : KType.Any;
                        if (listType != KType.Any && listType != KType.List && !listType.EndsWith("[]", StringComparison.Ordinal))
                            Report(text.ToSource(step.Companion.Value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                                $"through: takes a list of the values to go through, such as ['list', 'grid'], and this is {listType}.");
                    }
                    continue;
                }
                if (step.Action.Name == HandlerShorthands.Call)
                {
                    handlerShorthand = HandlerShorthands.Call;
                    try
                    {
                        AnalyzeStatements(Slice(text, step.Value.Value.Span), e, attr, scope, declare: false, requireEffect: true);
                    }
                    finally
                    {
                        handlerShorthand = null;
                    }
                    if (step.Companion != null)
                    {
                        AnalyzeExpressionRegion(Slice(text, step.Companion.Value.Span), RegionKind.Expression, scope, e, attr);
                    }
                    continue;
                }
                if (HandlerShorthands.Statement(step, text.Value) is not (string statement, int[] map)) continue;
                if (step.Action.FieldType is string wanted)
                {
                    // Check the field's type first, so a wrong field gets one message that names the action.
                    DecodedText fieldText = Slice(text, step.Field.Span);
                    ParseResult fieldParse = ExpressionParser.ParseExpression(fieldText.Value);
                    var probe = new ExpressionRegion { Kind = RegionKind.Expression, Source = fieldText, Parse = fieldParse, Scope = scope, Element = e, Attribute = attr };
                    string type = fieldParse.Success ? Infer(fieldParse.Root, probe).Type : KType.Any;
                    bool fits = type == KType.Any || type == wanted || wanted == KType.Number && type is "int" or "float"
                        || wanted == KType.List && (type == KType.List || type.EndsWith("[]", StringComparison.Ordinal));
                    if (!fits)
                    {
                        Report(text.ToSource(step.Field.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                            HandlerShorthands.FieldTypeMessage(step.Action, fieldText.Value, type));
                        continue;
                    }
                }
                AnalyzeStatements(new DecodedText(statement, map.Select(text.ToSource).ToArray()), e, attr, scope, declare: false, requireEffect: true);
            }
        }

        /// <summary>
        /// Checks <c>@class(...)</c>: each condition gives a bool (or a list of bools) and, with conditions first, each
        /// value gives a class name or a list of them.
        /// </summary>
        private void AnalyzeClassDirective(MarkupAttribute attr, MarkupElement e, Scope scope)
        {
            DecodedText text = attr.Value;
            ClassDirective.Reading reading = ClassDirective.Read(text.Value);
            foreach ((TextSpan span, string message) in reading.Problems)
            {
                Report(text.ToSource(span), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, "@class: " + message);
            }
            foreach (ClassDirective.Entry entry in reading.Entries)
            {
                ExpressionRegion condition = AnalyzeExpressionRegion(Slice(text, entry.Condition), RegionKind.Expression, scope, e, attr);
                if (condition.Parse.Success && condition.Parse.Root is ExprNode root)
                {
                    TypeShape shape = Infer(root, condition);
                    string type = root is ListNode list ? (list.Items.Select(i => Infer(i, condition).Type).FirstOrDefault(t => t is not (KType.Bool or KType.Any)) ?? KType.Bool) : shape.Type;
                    if (type is not (KType.Bool or KType.Any))
                    {
                        Report(text.ToSource(entry.Condition), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                            $"@class: {condition.Source.Value} must be true or false (or a list of them), and it is {type}.");
                    }
                }
                if (entry.ClassName != null) continue;
                ExpressionRegion classes = AnalyzeExpressionRegion(Slice(text, entry.Classes), RegionKind.Expression, scope, e, attr);
                if (classes.Parse.Success && classes.Parse.Root is LiteralNode { Kind: LiteralKind.String, Template: null } literal && !ClassDirective.IsClassName(literal.Value?.ToString() ?? string.Empty))
                {
                    Report(text.ToSource(entry.Classes), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"@class: '{literal.Value}' isn't a class name. A class has letters, digits, - and _.");
                }
            }
        }

        /// <summary>@show($open) and @live-disable(while: $saving): the condition gives true or false.</summary>
        private void AnalyzeConditionDirective(MarkupAttribute attr, MarkupElement e, Scope scope)
        {
            DecodedText text = attr.Value;
            TextSpan? span = new TextSpan(0, text.Value.Length);
            if (attr.Name == "live-disable")
            {
                (span, string problem) = Pages.LiveAttributes.DisableCondition(text.Value);
                if (problem != null) Report(text.SourceSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, problem);
            }
            if (span is not TextSpan condition || condition.Length == 0) return;
            ExpressionRegion region = AnalyzeExpressionRegion(Slice(text, condition), RegionKind.Expression, scope, e, attr);
            if (region.Parse.Success && region.Parse.Root is ExprNode root && Infer(root, region).Type is string type && type is not (KType.Bool or KType.Any))
            {
                Report(text.ToSource(condition), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                    $"@{(attr.Name == "show" ? "show" : "live-disable")} needs a condition that is true or false, and {region.Source.Value} is {type}.");
            }
        }

        /// <summary>Checks <c>@focus($cond)</c>: the condition gives a bool. <c>focus="{$cond}"</c> is read the same way.</summary>
        private void AnalyzeFocusDirective(MarkupAttribute attr, MarkupElement e, Scope scope)
        {
            DecodedText text = Unbraced(attr.Value);
            if (text.Value.Trim().Length == 0) return;
            ExpressionRegion condition = AnalyzeExpressionRegion(text, RegionKind.Expression, scope, e, attr);
            if (condition.Parse.Success && condition.Parse.Root is ExprNode root && Infer(root, condition).Type is string type && type is not (KType.Bool or KType.Any))
            {
                Report(text.ToSource(new TextSpan(0, text.Value.Length)), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                    $"@focus: {text.Value.Trim()} must be true or false, and it is {type}.");
            }
        }

        /// <summary>Checks <c>@pending('crumple', for: 'delete')</c>: a class (or list of them), then the methods whose actions count.</summary>
        private void AnalyzePendingDirective(MarkupAttribute attr, MarkupElement e, Scope scope)
        {
            DecodedText text = Unbraced(attr.Value);
            PendingDirective.Reading reading = PendingDirective.Read(text.Value);
            foreach ((TextSpan span, string message) in reading.Problems)
            {
                Report(text.ToSource(span), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, "@pending: " + message);
            }
            if (reading.Classes is TextSpan classSpan)
            {
                ExpressionRegion classes = AnalyzeExpressionRegion(Slice(text, classSpan), RegionKind.Expression, scope, e, attr);
                if (classes.Parse.Success && classes.Parse.Root is LiteralNode { Kind: LiteralKind.String, Template: null } literal && !ClassDirective.IsClassName(literal.Value?.ToString() ?? string.Empty))
                {
                    Report(text.ToSource(classSpan), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"@pending: '{literal.Value}' isn't a class name. A class has letters, digits, - and _.");
                }
            }
            if (reading.For is TextSpan forSpan)
            {
                ExpressionRegion methods = AnalyzeExpressionRegion(Slice(text, forSpan), RegionKind.Expression, scope, e, attr);
                if (methods.Parse.Success && methods.Parse.Root is VariableNode)
                {
                    Report(text.ToSource(forSpan), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"@pending: for: takes the method's name in quotes, for: '{text.Value.Substring(forSpan.Start, forSpan.Length).Trim().TrimStart('$')}'.");
                }
            }
        }

        /// <summary>Checks <c>@drag(front: true, copy: true, value: $c)</c>: known options, each of its type.</summary>
        private void AnalyzeDragDirective(MarkupAttribute attr, MarkupElement e, Scope scope)
        {
            DecodedText text = Unbraced(attr.Value);
            DragDirective.Reading reading = DragDirective.Read(text.Value);
            foreach ((TextSpan span, string message) in reading.Problems)
            {
                Report(text.ToSource(span), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, "@drag: " + message);
            }
            foreach ((DragDirective.Option option, ArgumentNode argument) in reading.Values)
            {
                ExpressionRegion value = AnalyzeExpressionRegion(Slice(text, argument.Value.Span), RegionKind.Expression, scope, e, attr);
                if (option.Type == KType.Any || !value.Parse.Success || value.Parse.Root is not ExprNode root) continue;
                string type = Infer(root, value).Type;
                bool fits = type == KType.Any || type == option.Type || option.Type == KType.Number && type is "int" or "float";
                if (!fits)
                {
                    Report(text.ToSource(argument.Value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                        $"@drag: {option.Name}: takes a {option.Type}, and {value.Source.Value.Trim()} is {type}.");
                }
            }
        }

        /// <summary>The expression inside <c>{ }</c> when the whole value is one, else the value as written.</summary>
        private static DecodedText Unbraced(DecodedText text)
        {
            string value = text.Value;
            int start = 0, end = value.Length;
            while (start < end && char.IsWhiteSpace(value[start])) start++;
            while (end > start && char.IsWhiteSpace(value[end - 1])) end--;
            if (end - start >= 3 && value[start] == '{' && value[end - 1] == '}' && value[start + 1] != '{')
                return Slice(text, TextSpan.FromBounds(start + 1, end - 1));
            return text;
        }

        /// <summary>Part of a decoded text, keeping each character's place in the source.</summary>
        private static DecodedText Slice(DecodedText text, TextSpan span) =>
            new DecodedText(text.Value.Substring(span.Start, span.Length), Enumerable.Range(span.Start, span.Length + 1).Select(text.ToSource).ToArray());
    }
}
