using KScript.Metadata;
using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    /// <summary>
    /// One element being run: its attributes and content, the scope it runs in, and helpers to evaluate attributes
    /// and run child elements. Element handlers in packages get one of these.
    /// </summary>
    public sealed class ElementRun
    {
        internal ElementRun(Interpreter interpreter, Module module, MarkupElement element, ElementDefinition definition, Scope scope, bool? chain)
        {
            Interpreter = interpreter;
            Module = module;
            Element = element;
            Definition = definition;
            Scope = scope;
            Chain = chain;
        }

        public Interpreter Interpreter { get; }
        public Module Module { get; }
        public MarkupElement Element { get; }
        public ElementDefinition Definition { get; }
        public Scope Scope { get; }

        /// <summary>What the element before this one returned: true if an &lt;if&gt;/&lt;elif&gt; branch ran, false if not, null otherwise.</summary>
        public bool? Chain { get; }

        public KScriptHost Host => Interpreter.Host;
        public string Name => Definition.Name;

        #region Attributes

        public bool Has(string attribute) => Element.GetAttribute(attribute) != null;

        /// <summary>The attribute's text exactly as written (entities decoded), or null.</summary>
        public string Raw(string attribute) => Element.GetAttribute(attribute)?.ValueText;

        /// <summary>The attribute's text, or an error saying it is needed.</summary>
        public string RequiredRaw(string attribute) => Raw(attribute) ?? throw Missing(attribute);

        /// <summary>A variable name written in an attribute (name="count", to="answer"), without a leading $.</summary>
        public string VariableName(string attribute, bool required = true)
        {
            string raw = required ? RequiredRaw(attribute) : Raw(attribute);
            if (raw == null) return null;
            string name = raw.Trim().TrimStart('$');
            if (name.Length == 0 || !Syntax.Lexer.IsIdentStart(name[0]) || !name.All(Syntax.Lexer.IsIdentChar))
                throw Error($"{attribute}=\"{raw}\" isn't a variable name. Use letters, digits and _, such as {attribute}=\"total\".", attribute);
            return name;
        }

        /// <summary>Evaluates an attribute as an expression.</summary>
        public object Eval(string attribute)
        {
            MarkupAttribute a = Element.GetAttribute(attribute) ?? throw Missing(attribute);
            if (a.Value == null) return true;
            return Interpreter.EvaluateText(a.Value, Scope, Module);
        }

        /// <summary>As <see cref="Eval(string)"/>, reading bare words as text where <paramref name="type"/> is read from text (<c>@kyle</c> for a handle).</summary>
        public object EvalTyped(string attribute, string type)
        {
            MarkupAttribute a = Element.GetAttribute(attribute) ?? throw Missing(attribute);
            if (a.Value == null) return true;
            return type == null ? Interpreter.EvaluateText(a.Value, Scope, Module) : Interpreter.EvaluateTyped(a.Value, Scope, Module, type);
        }

        /// <summary>As <see cref="EvalContent"/>, with bare words read as text for <paramref name="type"/>.</summary>
        public object EvalContentTyped(string type) =>
            !HasContent ? null : type == null ? Interpreter.EvaluateText(Content, Scope, Module) : Interpreter.EvaluateTyped(Content, Scope, Module, type);

        public object Eval(string attribute, object fallback) => Has(attribute) ? Eval(attribute) : fallback;

        public List<object> EvalList(string attribute)
        {
            MarkupAttribute a = Element.GetAttribute(attribute) ?? throw Missing(attribute);
            return Interpreter.EvaluateList(a.Value, Scope, Module);
        }

        /// <summary>The attribute with its <c>{ }</c> values and bare <c>$name</c> references filled in, as element text is.</summary>
        public string Template(string attribute, string fallback = null)
        {
            MarkupAttribute a = Element.GetAttribute(attribute);
            if (a == null) return fallback;
            if (a.Value == null) return string.Empty;
            return Interpreter.Interpolate(a.Value, Scope, Module, bareReferences: true);
        }

        public string RequiredTemplate(string attribute) => Template(attribute) ?? throw Missing(attribute);

        /// <summary>A yes/no attribute.</summary>
        public bool Flag(string attribute, bool fallback = false)
        {
            string raw = Raw(attribute);
            if (raw == null) return fallback;
            if (raw.Contains('{')) return Values.Truthy(Interpreter.InterpolateValue(Element.GetAttribute(attribute).Value, Scope, Module));
            return Values.ParseBool(raw) ?? throw Error($"{attribute}=\"{raw}\" should be true or false.", attribute);
        }

        /// <summary>
        /// A length of time, in seconds: a duration (<c>30s</c>, <c>500ms</c>, <c>1h30m</c>), text that reads as one, or
        /// a plain number of seconds. Null when the attribute isn't there.
        /// </summary>
        public double? Seconds(string attribute)
        {
            if (!Has(attribute)) return null;
            try
            {
                return Duration.Seconds(Eval(attribute), attribute);
            }
            catch (KScriptException ex) when (ex.Type == "Argument")
            {
                throw Error(ex.Message, attribute, "Argument");
            }
        }

        /// <summary>A number attribute written as an expression.</summary>
        public double Number(string attribute, double fallback) => Has(attribute) ? Values.ToNumber(Eval(attribute), $"<{Name} {attribute}>") : fallback;

        #endregion

        #region Content

        /// <summary>The element's text (comments skipped), or null.</summary>
        public DecodedText Content => Interpreter.ContentOf(Module, Element);

        public bool HasContent => Content is DecodedText t && t.Value.Trim().Length > 0;

        /// <summary>The element text as one expression.</summary>
        public object EvalContent() => HasContent ? Interpreter.EvaluateText(Content, Scope, Module) : null;

        /// <summary>The element text with <c>{ }</c> and bare <c>$name</c> filled in, trimmed and with common indentation removed.</summary>
        public string Text(Func<string, string> escape = null) => Content == null ? string.Empty : TextLayout.Tidy(Interpreter.Interpolate(Content, Scope, Module, bareReferences: true, escape, Values.ToStr, Definition.Info.LiteralText));

        /// <summary>As <see cref="Text"/>, turning each inserted value into text with <paramref name="format"/>.</summary>
        public string Text(Func<object, string> format) => Content == null ? string.Empty : TextLayout.Tidy(Interpreter.Interpolate(Content, Scope, Module, bareReferences: true, null, format, Definition.Info.LiteralText));

        /// <summary>True if the element text is one value and nothing else: <c>&lt;echo&gt;$person&lt;/echo&gt;</c>.</summary>
        public bool ContentIsSingleValue => Content != null && Interpreter.IsSingleValue(Content, Module, bareReferences: true);

        /// <summary>Runs the element text as assignment statements.</summary>
        public void RunStatements(bool declare, bool declareNew = false)
        {
            if (HasContent) Interpreter.ExecuteStatements(Content, Scope, Module, declare, declareNew);
        }

        public IEnumerable<MarkupElement> ChildElements => Element.ChildElements;

        /// <summary>Runs the child elements in a new child scope (or in <paramref name="scope"/>).</summary>
        public void RunChildren(Scope scope = null) => Interpreter.ExecuteChildren(Element, scope ?? Scope.Child(), Module);

        /// <summary>Runs some of the child elements.</summary>
        public void Run(IEnumerable<MarkupNode> nodes, Scope scope) => Interpreter.ExecuteNodes(nodes.ToList(), scope, Module);

        /// <summary>Runs one other element (for example a &lt;case&gt; or a &lt;catch&gt;) in a scope.</summary>
        public void RunElementChildren(MarkupElement element, Scope scope) => Interpreter.ExecuteChildren(element, scope, Module);

        #endregion

        #region Errors

        /// <summary>An error that points at this element, or at one of its attributes.</summary>
        public KScriptException Error(string message, string attribute = null, string type = "Error")
        {
            int? offset = attribute != null ? Element.GetAttribute(attribute)?.Span.Start : null;
            return new KScriptException(message, type) { Offset = offset ?? Element.Span.Start, OffsetModule = Module };
        }

        private KScriptException Missing(string attribute)
        {
            AttributeInfo info = Definition.Info.GetAttribute(attribute);
            return Error($"<{Name}> needs a {attribute} attribute{(info != null ? ": " + info.Description : ".")}", null, "Syntax");
        }

        #endregion
    }

    /// <summary>Tidies element text for output: trims it and removes the indentation the lines share.</summary>
    public static class TextLayout
    {
        public static string Tidy(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int start = 0, end = lines.Length - 1;
            while (start <= end && lines[start].Trim().Length == 0) start++;
            while (end >= start && lines[end].Trim().Length == 0) end--;
            if (start > end) return string.Empty;
            if (start == end) return lines[start].Trim();

            // The first line usually follows the opening tag directly, so it isn't counted for the indent.
            int indent = int.MaxValue;
            for (int i = start + 1; i <= end; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                indent = Math.Min(indent, lines[i].Length - lines[i].TrimStart().Length);
            }
            if (indent == int.MaxValue) indent = 0;
            var result = new List<string> { lines[start].Trim() };
            for (int i = start + 1; i <= end; i++)
            {
                string line = lines[i];
                result.Add((line.Length >= indent ? line.Substring(indent) : line.TrimStart()).TrimEnd());
            }
            return string.Join("\n", result);
        }
    }
}
