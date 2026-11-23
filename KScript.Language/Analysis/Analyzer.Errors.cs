using KScript.Language.Catalog;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Error types: &lt;error&gt; declarations (<c>error NotFound(status: 404) extends Validation { id: int }</c>), the
    /// names in <c>catch</c>, <c>throw</c>, <c>extends</c> and <c>expect_error</c>, and calls that make errors
    /// (<c>NotFound(id: 42)</c>). A caught error completes the fields of the types its catch names.
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <summary>The fields every caught error has.</summary>
        private static readonly string[] BaseErrorFields = { "type", "message", "line", "column", "file", "status", "cause" };

        /// <summary>The error types this document declares, by name.</summary>
        private readonly Dictionary<string, DeclaredError> declaredErrors = new Dictionary<string, DeclaredError>(StringComparer.Ordinal);

        /// <summary>The error types other scripts export to this one and the type each extends.</summary>
        private readonly Dictionary<string, string> importedErrors = new(StringComparer.Ordinal);

        private sealed class DeclaredError
        {
            public MarkupElement Element;
            public Symbol Symbol;
            public string Parent;
            public TypeShape Own = new TypeShape(KType.Map);
            public HashSet<string> Required = new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>True if <paramref name="name"/> is a built-in error type (names are matched without case, as at run time).</summary>
        private static bool IsBuiltInError(string name) => ErrorTypes.BuiltIn.ContainsKey(name);

        /// <summary>
        /// Declares every &lt;error&gt; before the walk, as the runtime hoists them, so a catch or a throw can name an
        /// error type declared further down, and error types can extend each other in any order.
        /// </summary>
        private void HoistErrors(Scope root)
        {
            foreach (MarkupElement e in result.Markup.DescendantElements().Where(e => IsKScriptElementName(e, "error")))
            {
                MarkupAttribute nameAttr = e.GetAttribute("name");
                if (nameAttr?.Value == null || !CheckIdentifier(nameAttr.Value))
                {
                    continue;
                }
                string name = nameAttr.ValueText.Trim().TrimStart('$');
                TextSpan span = TrimmedSpan(nameAttr.Value);
                if (!char.IsUpper(name[0]))
                {
                    string fixedName = char.ToUpperInvariant(name[0]) + name.Substring(1);
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"Error type names start with a capital letter, so catch can tell them from variables: {fixedName}.",
                        new[] { new QuickFix($"Change to {fixedName}", new TextChange(span, fixedName)) });
                    continue;
                }
                if (IsBuiltInError(name))
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"{name} is a built-in error type. Give yours another name, and extend it: error My{name} extends {name}.");
                    continue;
                }
                if (declaredErrors.ContainsKey(name))
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"An error type named '{name}' is already declared.");
                    continue;
                }
                var declared = new DeclaredError { Element = e, Parent = e.GetAttributeValue("extends")?.Trim() };
                if (string.IsNullOrEmpty(declared.Parent))
                {
                    declared.Parent = null;
                }
                TypeShape instance = new TypeShape(KType.Error) { Name = name, Closed = true };
                declared.Symbol = NewVariable(name, span, e, SymbolKind.Constant, new TypeShape(KType.ErrorType) { Name = name, Instance = instance });
                root.Declare(declared.Symbol, 0);
                declaredErrors[name] = declared;
                FillFields(e, declared.Own);
                NoteRequiredFields(declared);
            }

            foreach (DeclaredError declared in declaredErrors.Values)
            {
                List<DeclaredError> chain = ErrorChain(declared);
                TypeShape instance = declared.Symbol.Shape.Instance;
                foreach (KeyValuePair<string, FieldInfo> f in BaseErrorShape().Fields)
                {
                    instance.Fields[f.Key] = f.Value;
                }
                foreach (DeclaredError d in Enumerable.Reverse(chain))
                {
                    foreach (KeyValuePair<string, FieldInfo> f in d.Own.Fields)
                    {
                        instance.Fields[f.Key] = f.Value;
                    }
                }
                string parent = chain.Count > 1 ? chain[1].Symbol.Name : declared.Parent;
                int? status = chain.Select(d => int.TryParse(d.Element.GetAttributeValue("status")?.Trim(), out int s) ? s : (int?)null).FirstOrDefault(s => s != null)
                    ?? (chain.Count > 0 && chain[chain.Count - 1].Parent is string top && ErrorTypes.BuiltIn.TryGetValue(top, out ErrorType b) ? b.Status : null);
                string description = declared.Element.GetAttributeValue("description");
                declared.Symbol.Description = (description != null ? description + "\n\n" : string.Empty)
                    + "error " + declared.Symbol.Name + (parent != null ? " extends " + parent : string.Empty) + (status != null ? $" (status {status})" : string.Empty)
                    + (declared.Own.Fields.Count == 0 && chain.Count == 1 ? string.Empty
                        : " {\n" + string.Concat(instance.Fields.Values.Where(f => !BaseErrorFields.Contains(f.Name)).Select(f => "  " + f.Name + ": " + f.Shape.Display() + "\n")) + "}");
            }
        }

        /// <summary>Which of an error's own fields a throw must give: those without ?, a default or optional="yes".</summary>
        private static void NoteRequiredFields(DeclaredError declared)
        {
            MarkupElement e = declared.Element;
            MarkupAttribute fields = e.GetAttribute("fields");
            if (fields?.Value != null)
            {
                foreach ((string part, int _) in SplitTopLevel(fields.Value.Value))
                {
                    string head = part.Split('=')[0];
                    string name = head.Split(':')[0].Trim();
                    if (!part.Contains('=') && !name.EndsWith("?", StringComparison.Ordinal) && !head.TrimEnd().EndsWith("?", StringComparison.Ordinal))
                    {
                        declared.Required.Add(name);
                    }
                }
            }
            foreach (MarkupElement field in e.ChildElements.Where(c => c.Name == "field"))
            {
                string name = field.GetAttributeValue("name")?.Trim();
                string type = field.GetAttributeValue("type")?.Trim();
                if (string.IsNullOrEmpty(name) || name.EndsWith("?", StringComparison.Ordinal) || (type != null && type.EndsWith("?", StringComparison.Ordinal))
                    || IsYes(field.GetAttributeValue("optional")) || field.GetAttribute("default") != null || field.GetAttribute("value") != null)
                {
                    continue;
                }
                declared.Required.Add(name);
            }
        }

        /// <summary>The error and the declared error types it extends, nearest first. Stops at a built-in type or a loop.</summary>
        private List<DeclaredError> ErrorChain(DeclaredError start)
        {
            var chain = new List<DeclaredError>();
            for (DeclaredError d = start; d != null && !chain.Contains(d); d = d.Parent != null && declaredErrors.TryGetValue(d.Parent, out DeclaredError p) ? p : null)
            {
                chain.Add(d);
            }
            return chain;
        }

        /// <summary>Walks an &lt;error&gt;: its fields, status and extends, and its message with the fields as variables.</summary>
        private void AnalyzeError(MarkupElement e, ElementInfo info, Scope scope, Scope inner, bool markupMode)
        {
            AnalyzeAttributes(e, info, scope, skip: "message");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            MarkupAttribute fields = e.GetAttribute("fields");
            if (fields?.Value != null)
            {
                foreach ((string part, int start) in SplitTopLevel(fields.Value.Value))
                {
                    CheckInlineField(part, start, fields.Value, seen);
                }
            }
            foreach (MarkupElement child in e.ChildElements)
            {
                if (!IsKScriptElementName(child, "field"))
                {
                    Report(child.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, $"<error> can only hold <field> elements, not <{child.Name}>.");
                    continue;
                }
                MarkupAttribute nameAttr = child.GetAttribute("name");
                string name = nameAttr?.ValueText?.Trim().TrimEnd('?').TrimEnd();
                if (!string.IsNullOrEmpty(name) && !seen.Add(name))
                {
                    Report(TrimmedSpan(nameAttr.Value), DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"The field '{name}' is listed twice.");
                }
            }
            string reserved = seen.FirstOrDefault(f => BaseErrorFields.Contains(f));
            if (reserved != null)
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{reserved}' is part of every error, so an error type can't have a field with that name.");
            }

            MarkupAttribute status = e.GetAttribute("status");
            if (status?.Value != null && (!int.TryParse(status.ValueText.Trim(), out int code) || code < 100 || code > 599))
            {
                Report(TrimmedSpan(status.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"'{status.ValueText.Trim()}' isn't an HTTP status. Use a number from 100 to 599, such as 404.");
            }

            string name0 = e.GetAttributeValue("name")?.Trim();
            if (name0 != null && declaredErrors.TryGetValue(name0, out DeclaredError declared) && declared.Element == e && declared.Parent != null)
            {
                List<DeclaredError> chain = ErrorChain(declared);
                DeclaredError last = chain[chain.Count - 1];
                if (last.Parent != null && declaredErrors.TryGetValue(last.Parent, out DeclaredError loop) && chain.Contains(loop))
                {
                    Report(TrimmedSpan(e.GetAttribute("extends").Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"{name0} extends itself through {declared.Parent}. Each error type extends one that doesn't lead back to it.");
                }
            }

            MarkupAttribute message = e.GetAttribute("message");
            if (message?.Value != null)
            {
                Scope messageScope = new Scope(scope, e.Span);
                TypeShape instance = name0 != null && declaredErrors.TryGetValue(name0, out DeclaredError d) && d.Element == e ? d.Symbol.Shape.Instance : new TypeShape(KType.Error);
                foreach (FieldInfo f in instance.Fields.Values.Where(f => !BaseErrorFields.Contains(f.Name)))
                {
                    Symbol symbol = new Symbol { Name = f.Name, Kind = SymbolKind.Parameter, Shape = f.Shape, Description = f.Description ?? $"The {f.Name} field.", DeclaringElement = e, DeclarationSpan = f.Declaration ?? default };
                    result.Symbols.Add(symbol);
                    messageScope.Declare(symbol, e.Span.Start);
                }
                AnalyzeTemplate(message.Value, messageScope, e, message, allowBare: true);
            }
            WalkChildren(e.Children, inner, markupMode);
        }

        /// <summary>The names in an error type attribute, with their spans: <c>NotFound</c>, <c>IO, Network</c>, <c>IO | Network</c>, <c>[IO, Network]</c>.</summary>
        private static IEnumerable<(string Name, TextSpan Span)> ErrorTypeNames(DecodedText value)
        {
            string text = value.Value;
            int i = 0;
            while (i < text.Length)
            {
                if (!Lexer.IsIdentStart(text[i]))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && Lexer.IsIdentChar(text[i]))
                {
                    i++;
                }
                yield return (text.Substring(start, i - start), value.ToSource(new TextSpan(start, i - start)));
            }
        }

        /// <summary>Checks the names in a catch's, throw's, extends' or expect_error's type: each must be an error type.</summary>
        private void CheckErrorTypeAttribute(MarkupElement e, MarkupAttribute attr, Scope scope)
        {
            string text = attr.ValueText.Trim();
            if (text.Length == 0)
            {
                return;
            }
            foreach ((string name, TextSpan span) in ErrorTypeNames(attr.Value))
            {
                if (ErrorTypes.BuiltIn.TryGetValue(name, out ErrorType b) && b.Name != name && scope.Lookup(name, span.Start) == null)
                {
                    // The runtime matches built-in names without case, but the editor asks for one spelling.
                    Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownErrorType, $"Write the error type as {b.Name}.",
                        new[] { new QuickFix($"Change to {b.Name}", new TextChange(span, b.Name)) });
                    continue;
                }
                if (ReferenceErrorType(name, span, scope))
                {
                    continue;
                }
                if (ErrorTypes.Renamed.TryGetValue(name, out string now))
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownErrorType, ErrorTypes.RenamedMessage(name),
                        new[] { new QuickFix($"Change to {now}", new TextChange(span, now)) });
                    continue;
                }
                IEnumerable<string> known = KnownErrorNames(scope, span.Start);
                string close = Suggestions.Closest(name, known);
                Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.UnknownErrorType,
                    Wording.Of($"Unknown error type '{name}'.") + (close != null ? $" Did you mean {close}?" : string.Empty)
                    + Wording.Of($" Declare it with <error name=\"{name}\" />, or use a built-in one such as Validation, NotFound or IO."),
                    SuggestFix(span, name, known, n => n));
            }
        }

        /// <summary>True if <paramref name="name"/> is an error type here; a declared one also gets a reference.</summary>
        private bool ReferenceErrorType(string name, TextSpan span, Scope scope)
        {
            Symbol symbol = scope.Lookup(name, span.Start);
            if (symbol?.Shape.Type == KType.ErrorType)
            {
                result.References.Add(new SymbolReference(symbol, span, false, false));
                return true;
            }
            return IsBuiltInError(name) || importedErrors.ContainsKey(name);
        }

        /// <summary>The error types visible at <paramref name="offset"/>: declared, imported and built-in.</summary>
        private IEnumerable<string> KnownErrorNames(Scope scope, int offset) =>
            scope.VisibleAt(offset).Where(s => s.Shape.Type == KType.ErrorType).Select(s => s.Name).Concat(importedErrors.Keys).Concat(ErrorTypes.BuiltIn.Values.Select(t => t.Name)).Distinct();

        /// <summary>The fields every error has, and those of a Shape error.</summary>
        internal static TypeShape BaseErrorShape(int depth = 0)
        {
            TypeShape shape = new TypeShape(KType.Error);
            foreach (string f in BaseErrorFields)
            {
                TypeShape fieldShape = f switch
                {
                    "line" or "column" or "status" => new TypeShape(KType.Number),
                    // A cause is an error too; past a few levels it's left open rather than built forever.
                    "cause" => depth < 3 ? BaseErrorShape(depth + 1) : TypeShape.Unknown,
                    _ => new TypeShape(KType.String),
                };
                shape.Fields[f] = new FieldInfo(f, fieldShape, null, f switch
                {
                    "status" => "The HTTP status its error type carries, such as 404, or null.",
                    "cause" => "The error that led to this one, or null.",
                    _ => null,
                });
            }
            return shape;
        }

        /// <summary>
        /// What a catch (or expect_error) declares: an error with the fields of the types it names, so
        /// <c>catch NotFound as e</c> completes <c>$e.id</c>. With no types, any error.
        /// </summary>
        private TypeShape CaughtErrorShape(MarkupElement e, Scope scope)
        {
            TypeShape shape = ErrorShape();
            MarkupAttribute type = e.GetAttribute("type");
            if (type?.Value == null)
            {
                return shape;
            }
            foreach ((string name, TextSpan span) in ErrorTypeNames(type.Value))
            {
                if (scope.Lookup(name, span.Start) is Symbol { Shape.Type: KType.ErrorType } symbol)
                {
                    foreach (KeyValuePair<string, FieldInfo> f in symbol.Shape.Instance.Fields)
                    {
                        shape.Fields.TryAdd(f.Key, f.Value);
                    }
                }
            }
            return shape;
        }

        /// <summary>
        /// <c>NotFound(id: 42)</c>: a call that makes an error. Its named arguments must be the type's fields (plus
        /// message and cause), and the fields without a default must be given.
        /// </summary>
        private bool AnalyzeErrorCall(CallNode call, IdentifierNode id, ExpressionRegion region, Scope scope)
        {
            TextSpan span = region.Source.ToSource(id.Span);
            Symbol symbol = scope.Lookup(id.Name, span.Start);
            bool declared = symbol?.Shape.Type == KType.ErrorType;
            if (!declared && !(IsBuiltInError(id.Name) && char.IsUpper(id.Name[0])) && !importedErrors.ContainsKey(id.Name))
            {
                return false;
            }
            if (declared)
            {
                result.References.Add(new SymbolReference(symbol, span, false, false));
            }
            TextSpan callSpan = region.Source.ToSource(call.Span);
            if (call.Arguments.Count(a => a.Name == null) > 1)
            {
                Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount, $"{id.Name}() takes its fields by name, such as {id.Name}(id: 42), and at most one message.");
            }
            if (!declared)
            {
                return true;
            }

            DeclaredError error = declaredErrors.TryGetValue(id.Name, out DeclaredError d) && d.Symbol == symbol ? d : null;
            List<string> fields = symbol.Shape.Instance.Fields.Keys.Where(f => !BaseErrorFields.Contains(f)).ToList();
            foreach (ArgumentNode arg in call.Arguments.Where(a => a.Name != null && a.Name != "message" && a.Name != "cause"))
            {
                if (fields.Contains(arg.Name))
                {
                    continue;
                }
                TextSpan nameSpan = region.Source.ToSource(arg.NameSpan);
                string close = Suggestions.Closest(arg.Name, fields);
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter,
                    $"{id.Name} has no field '{arg.Name}'." + (close != null ? $" Did you mean {close}?" : fields.Count > 0 ? $" Its fields are: {string.Join(", ", fields)}." : $" {id.Name} has no fields."),
                    SuggestFix(nameSpan, arg.Name, fields, n => n));
            }
            if (error != null)
            {
                List<string> missing = ErrorChain(error).SelectMany(c => c.Required).Distinct().Where(f => !call.Arguments.Any(a => a.Name == f)).ToList();
                if (missing.Count > 0)
                {
                    Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                        $"{id.Name}() needs {string.Join(" and ", missing)}: {id.Name}({string.Join(", ", missing.Select(m => m + ": ..."))}).");
                }
            }
            return true;
        }

        /// <summary>A bare error type name in an expression (<c>$err->is(NotFound)</c>) is the type, not a word.</summary>
        private bool ReferenceBareErrorType(IdentifierNode node, ExpressionRegion region, Scope scope)
        {
            TextSpan span = region.Source.ToSource(node.Span);
            Symbol symbol = scope.Lookup(node.Name, span.Start);
            if (symbol?.Shape.Type == KType.ErrorType)
            {
                result.References.Add(new SymbolReference(symbol, span, false, false));
                return true;
            }
            return ErrorTypes.BuiltIn.TryGetValue(node.Name, out ErrorType b) && b.Name == node.Name || importedErrors.ContainsKey(node.Name);
        }
    }
}
