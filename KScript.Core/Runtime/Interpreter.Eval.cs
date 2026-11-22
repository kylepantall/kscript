using KScript.Metadata;
using System.Globalization;
using System.Text;

using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    public sealed partial class Interpreter
    {
        #region Entry points

        /// <summary>Evaluates an attribute or element text as one expression.</summary>
        public object EvaluateText(DecodedText text, Scope scope, Module module)
        {
            ParseResult parsed = GetParse(module, text, ParseMode.Expression);
            return Evaluate(parsed.Root, scope, new EvalContext(module, text));
        }

        /// <summary>
        /// Evaluates a value written for a declared type. Where the type is read from text (a handle, an enum or a type with a
        /// pattern), a bare word such as <c>@kyle</c>, <c>red</c> or <c>H4cmW12in</c> is taken as the text itself, the way a
        /// regex literal needs no quotes. Anything else is evaluated as usual.
        /// </summary>
        public object EvaluateTyped(DecodedText text, Scope scope, Module module, string type)
        {
            TypeRef typeRef = TypeRef.Parse(type);
            string kind = Shapes.RawTextKind(typeRef, scope, module);
            // semver[] = [1.0.0, 1.0.1-rc]: in a list of a type read from text, each item may be written raw too.
            if (kind == null && typeRef is { ListDepth: 1 } && RawListItems(text.Value) is List<string> items
                && Shapes.RawTextKind(TypeRef.Parse(type.Trim()[..^2]), scope, module) is string itemKind)
                return items.Select(item => RawOrEvaluated(item, itemKind, text, scope, module)).ToList();
            if (kind == null) return EvaluateText(text, scope, module);
            ParseResult parsed = ExpressionParser.ParseExpression(text.Value);
            if (!parsed.Success || IsRawWord(parsed.Root, kind, scope, module) || LooksRaw(text.Value, kind)) return text.Value.Trim();
            return EvaluateText(text, scope, module);
        }

        private object RawOrEvaluated(string item, string kind, DecodedText whole, Scope scope, Module module)
        {
            ParseResult parsed = ExpressionParser.ParseExpression(item);
            if (!parsed.Success || IsRawWord(parsed.Root, kind, scope, module) || LooksRaw(item, kind)) return item.Trim();
            return Evaluate(parsed.Root, scope, new EvalContext(module, whole));
        }

        /// <summary>
        /// The items of a list literal <c>[a, b]</c>, split at its top-level commas (not inside quotes or brackets), or
        /// null when the text isn't one list literal.
        /// </summary>
        internal static List<string> RawListItems(string text)
        {
            string t = text.Trim();
            if (t.Length < 2 || t[0] != '[' || t[^1] != ']') return null;
            var items = new List<string>();
            int depth = 0, start = 1;
            char quote = '\0';
            for (int i = 1; i < t.Length - 1; i++)
            {
                char c = t[i];
                if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; continue; }
                switch (c)
                {
                    case '\'': case '"': quote = c; break;
                    case '[': case '(': case '{': depth++; break;
                    case ']': case ')': case '}': if (--depth < 0) return null; break;
                    case ',' when depth == 0: items.Add(t[start..i]); start = i + 1; break;
                }
            }
            if (depth != 0 || quote != '\0') return null;
            string last = t[start..^1];
            if (last.Trim().Length > 0 || items.Count > 0) items.Add(last);
            return items.Any(x => x.Trim().Length == 0) ? null : items;
        }

        private static readonly System.Text.RegularExpressions.Regex RawHandle = new System.Text.RegularExpressions.Regex(@"^@[\w.\-]+$");

        /// <summary>Text that is plainly a handle (<c>@a--b</c>, which would otherwise read as @a - -b), for a handle type.</summary>
        internal static bool LooksRaw(string text, string kind) => kind == "handle" && RawHandle.IsMatch(text.Trim());

        /// <summary>True if <paramref name="node"/> is only bare words, numbers and the operators between them (<c>my-post</c>, <c>H4cmW12in</c>, <c>Europe/London</c>).</summary>
        internal static bool IsRawWord(ExprNode node, string kind, Scope scope, Module module)
        {
            switch (node)
            {
                case IdentifierNode id:
                    return !(module != null && module.Aliases.ContainsKey(id.Name));
                case CommandCallNode { ArgumentList: null }:
                    return kind == "handle";
                case MemberNode { NullSafe: false } member when kind == "word":
                    // example.com, api.example.co.uk
                    return IsRawPart(member.Target, kind, scope, module);
                case BinaryNode bin when bin.Operator == "-" || bin.Operator == "." || bin.Operator == "+" || bin.Operator == "/":
                    return IsRawWord(bin.Left, kind, scope, module) && IsRawPart(bin.Right, kind, scope, module)
                        || IsRawPart(bin.Left, kind, scope, module) && IsRawWord(bin.Right, kind, scope, module);
                default:
                    return false;
            }
        }

        private static bool IsRawPart(ExprNode node, string kind, Scope scope, Module module) =>
            node is LiteralNode { Kind: LiteralKind.Number } || IsRawWord(node, kind, scope, module);

        /// <summary>Evaluates comma separated expressions (<c>is="2, 3"</c>).</summary>
        public List<object> EvaluateList(DecodedText text, Scope scope, Module module)
        {
            ParseResult parsed = GetParse(module, text, ParseMode.List);
            var ctx = new EvalContext(module, text);
            return parsed.Nodes.Select(n => Evaluate(n, scope, ctx)).ToList();
        }

        /// <summary>Runs <c>x = 3; total += $price</c> statements. With <paramref name="declare"/>, plain names are declared (as &lt;let&gt; does).</summary>
        /// <summary>
        /// Runs assignment statements and calls. <paramref name="declare"/> declares every assigned name in this scope
        /// (&lt;let&gt;); <paramref name="declareNew"/> declares only names not yet in scope and changes the rest
        /// (.kscript files, where <c>x = 3</c> is the one way to assign).
        /// </summary>
        public void ExecuteStatements(DecodedText text, Scope scope, Module module, bool declare, bool declareNew = false)
        {
            ParseResult parsed = GetParse(module, text, ParseMode.Statements);
            var ctx = new EvalContext(module, text);
            foreach (ExprNode node in parsed.Nodes)
            {
                if (node is AssignmentNode assignment) Assign(assignment, scope, ctx, declare || (declareNew && assignment.Operator == "=" && assignment.Target is VariableNode or IdentifierNode && !scope.Has(assignment.Name)));
                else Evaluate(node, scope, ctx);
            }
        }

        /// <summary>Replaces <c>{ expr }</c> (and, in element text, bare <c>$name</c>) in text. <paramref name="escape"/> is applied to inserted values, except <c>{! raw }</c>.</summary>
        public string Interpolate(DecodedText text, Scope scope, Module module, bool bareReferences, Func<string, string> escape = null) =>
            Interpolate(text, scope, module, bareReferences, escape, Values.ToStr);

        /// <summary>As <see cref="Interpolate(DecodedText, Scope, Module, bool, Func{string, string})"/>, turning each value into text with <paramref name="format"/>.</summary>
        public string Interpolate(DecodedText text, Scope scope, Module module, bool bareReferences, Func<string, string> escape, Func<object, string> format, TextLiterals literals = TextLiterals.None)
        {
            if (text == null) return string.Empty;
            Template template = GetTemplate(module, text, bareReferences, literals: literals);
            if (!template.Expressions.Any()) return string.Concat(template.Segments.Select(s => s.Text));
            var ctx = new EvalContext(module, text);
            var sb = new StringBuilder(text.Value.Length + 16);
            foreach (TemplateSegment segment in template.Segments)
            {
                switch (segment.Kind)
                {
                    case TemplateSegmentKind.Text:
                        sb.Append(segment.Text);
                        break;
                    case TemplateSegmentKind.RawInterpolation:
                        sb.Append(format(Evaluate(segment.Node, scope, ctx)));
                        break;
                    default:
                        string value = format(Evaluate(segment.Node, scope, ctx));
                        sb.Append(escape != null ? escape(value) : value);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>True if the text is one value and nothing else: <c>{ $person }</c> or a bare <c>$person</c> in element text.</summary>
        public bool IsSingleValue(DecodedText text, Module module, bool bareReferences)
        {
            if (text == null) return false;
            Template template = GetTemplate(module, text, bareReferences);
            return template.Expressions.Count() == 1 && template.Segments.All(s => s.Kind != TemplateSegmentKind.Text || string.IsNullOrWhiteSpace(s.Text));
        }

        /// <summary>
        /// If the text is exactly one <c>{ expr }</c>, evaluates it to a value (a list stays a list); otherwise
        /// interpolates it to text. Used for attributes that pass values, such as component props.
        /// </summary>
        public object InterpolateValue(DecodedText text, Scope scope, Module module)
        {
            if (text == null) return null;
            Template template = GetTemplate(module, text, bareReferences: false);
            if (template.IsSingleInterpolation)
            {
                return Evaluate(template.Expressions.First().Node, scope, new EvalContext(module, text));
            }
            return Interpolate(text, scope, module, bareReferences: false);
        }

        #endregion

        #region Evaluation

        private KScriptException Fail(ExprNode node, EvalContext ctx, Wording message, string type = "Error")
        {
            return new KScriptException(message, type) { Offset = node == null ? null : ctx.ToSource(node.Span.Start), OffsetModule = ctx.Module };
        }

        private static string SourceOf(ExprNode node, EvalContext ctx)
        {
            string text = ctx.Origin?.Value;
            if (text == null || node.Span.End > text.Length) return null;
            return text.Substring(node.Span.Start, node.Span.Length).Trim();
        }

        /// <summary>Evaluates an expression node.</summary>
        public object Evaluate(ExprNode node, Scope scope, EvalContext ctx)
        {
            Step();
            try
            {
                return EvaluateCore(node, scope, ctx);
            }
            catch (KScriptException ex) when (ex.Offset == null && !ex.HasLocation)
            {
                ex.Offset = ctx.ToSource(node.Span.Start);
                ex.OffsetModule = ctx.Module;
                throw;
            }
            catch (Exception ex) when (ex is not ControlSignal && ex is not KScriptException && Wrap(ex) is KScriptException wrapped)
            {
                wrapped.Offset = ctx.ToSource(node.Span.Start);
                wrapped.OffsetModule = ctx.Module;
                throw wrapped;
            }
        }

        private object EvaluateCore(ExprNode node, Scope scope, EvalContext ctx)
        {
            switch (node)
            {
                case LiteralNode lit:
                    return lit.Kind switch
                    {
                        LiteralKind.Number => double.Parse(lit.Value, NumberStyles.Float, CultureInfo.InvariantCulture),
                        LiteralKind.String => lit.Template == null ? lit.Value : FillIn(lit.Template, scope, ctx),
                        LiteralKind.Bool => lit.Value == "true",
                        LiteralKind.Regex or LiteralKind.Email or LiteralKind.Uuid or LiteralKind.Url or LiteralKind.Duration => TypedLiteral(lit, ctx),
                        _ => null,
                    };

                case VariableNode v:
                    if (scope.TryGet(v.Name, out object value))
                    {
                        // $Repo for the declared type Repo: a type is named bare. (t = Repo; $t is a variable, read with $.)
                        if (value is IDeclaredType declared && declared.Name == v.Name && v.Span.Length > 1 && ctx.Module?.IsCode == true)
                            throw Fail(node, ctx, KScript.Syntax.Markup.OldSpellings.TypeIsBare(v.Name), "Syntax");
                        return value;
                    }
                    // A package's variable ($config, $session) in a page or other document that isn't run as a script.
                    if (ctx.Module != null && ctx.Module.Variables.TryGetValue(v.Name, out VariableDefinition packageVariable)) return packageVariable.Value(this, ctx.Module);
                    throw Fail(node, ctx, UndefinedMessage(v.Name, scope), "Name");

                case IdentifierNode id:
                    if (ctx.Module != null && ctx.Module.Aliases.TryGetValue(id.Name, out ImportNamespace ns)) return ns;
                    // A declared type (type, model, enum, error, job) is named bare: as: Repo, User->find(1), Size.small.
                    if (scope.TryGet(id.Name, out object declaredType) && declaredType is IDeclaredType) return declaredType;
                    // So is an error type: catch NotFound, $err->is(NotFound).
                    if (ErrorTypes.FindExact(id.Name, scope) is ErrorType errorType) return errorType;
                    throw Fail(node, ctx, $"Unknown name '{id.Name}'. Variables start with $ (${id.Name}) and commands with @ (@{id.Name}(...)).", "Name");

                case ListNode list:
                    return list.Items.Select(i => Evaluate(i, scope, ctx)).ToList();

                case MapNode map:
                {
                    var dict = new Dictionary<string, object>();
                    foreach (MapEntryNode entry in map.Entries) dict[entry.Key] = Evaluate(entry.Value, scope, ctx);
                    return dict;
                }

                case MemberNode member:
                {
                    object target = Evaluate(member.Target, scope, ctx);
                    if (target == null)
                    {
                        if (member.NullSafe) return null;
                        throw Fail(member, ctx, $"Can't read .{member.Name} of null{Where(member.Target, ctx)}. Use ?. if it may be missing: {SourceOf(member.Target, ctx)}?.{member.Name}", "Null");
                    }
                    return GetMember(target, member.Name, member, ctx);
                }

                case IndexNode index:
                {
                    object target = Evaluate(index.Target, scope, ctx);
                    object key = Evaluate(index.Index, scope, ctx);
                    return GetIndex(target, key, index, ctx);
                }

                case MethodCallNode method:
                {
                    object target = Evaluate(method.Target, scope, ctx);
                    (List<object> args, Dictionary<string, object> named) = EvaluateArguments(method.Arguments, scope, ctx, method.Name);
                    return InvokeMethod(target, method.Name, args, named, scope, ctx, method);
                }

                case CommandCallNode command:
                {
                    if (command.ArgumentList == null)
                    {
                        // @greet without parentheses is the function itself: $people->map(@greet).
                        object found = ResolveCallable(command.Name, scope, ctx, command);
                        return found is CommandDefinition cmd ? new NativeFunction(cmd.Name, call => InvokeCommand(cmd, call)) : found;
                    }
                    (List<object> args, Dictionary<string, object> named) = EvaluateArguments(command.Arguments, scope, ctx, command.Name);
                    return CallCommand(command.Name, args, named, scope, ctx, command, new CallSite(command, command.Arguments, ctx));
                }

                case CallNode call:
                    return EvaluateCall(call, NotPiped, scope, ctx);

                case PipeNode pipe:
                    return EvaluatePipe(pipe, scope, ctx);

                case UnaryNode unary:
                {
                    object operand = Evaluate(unary.Operand, scope, ctx);
                    switch (unary.Operator)
                    {
                        case "not": return !Values.Truthy(operand);
                        case "-" when NativeTypeRegistry.TryNegate(operand, out object negated): return negated;
                        case "-": return -Values.ToNumber(operand, $"'-'{Where(unary, ctx)}");
                        default: return Values.ToNumber(operand, $"'+'{Where(unary, ctx)}");
                    }
                }

                case BinaryNode binary:
                    return EvaluateBinary(binary, scope, ctx);

                case ConditionalNode conditional:
                    return Values.Truthy(Evaluate(conditional.Condition, scope, ctx))
                        ? Evaluate(conditional.WhenTrue, scope, ctx)
                        : Evaluate(conditional.WhenFalse, scope, ctx);

                case LambdaNode lambda:
                    return new Lambda(lambda, scope, ctx, SourceOf(lambda, ctx));

                case RangeNode range:
                    return EvaluateRange(range, scope, ctx);

                case MatchNode match:
                {
                    object subject = Evaluate(match.Subject, scope, ctx);
                    foreach (MatchArmNode arm in match.Arms)
                    {
                        if ((arm.Patterns.Count == 0 || arm.Patterns.Any(p => Matches(subject, p, scope, ctx)))
                            && (arm.Guard == null || Values.Truthy(Evaluate(arm.Guard, scope, ctx))))
                        {
                            return Evaluate(arm.Result, scope, ctx);
                        }
                    }
                    return null;
                }

                case AssignmentNode assignment:
                    throw Fail(assignment, ctx, $"Assignment isn't allowed here. Use == (or ===) to compare; assign with <let>, <set> or <do>.", "Syntax");

                case ErrorNode:
                    throw Fail(node, ctx, "This expression has a syntax error.", "Syntax");
            }
            throw Fail(node, ctx, $"Can't evaluate {node.GetType().Name}.");
        }

        private static string Where(ExprNode node, EvalContext ctx)
        {
            string source = SourceOf(node, ctx);
            return string.IsNullOrEmpty(source) ? string.Empty : $" in '{source}'";
        }

        private static Wording UndefinedMessage(string name, Scope scope)
        {
            string close = Values.Closest(name, scope.Names());
            return Wording.Of($"${name} is not defined.") + (close != null ? $" Did you mean ${close}?" : Wording.Of($" Declare it with <let name=\"{name}\" value=\"...\" />."));
        }

        private bool Matches(object subject, ExprNode pattern, Scope scope, EvalContext ctx)
        {
            object p = Evaluate(pattern, scope, ctx);
            if ((pattern is ListNode || pattern is RangeNode) && p is List<object> options) return options.Any(o => Values.LooseEquals(o, subject));
            return Values.LooseEquals(p, subject);
        }

        /// <summary>"Hello $name, { $n * 2 }": each part worked out and joined.</summary>
        private string FillIn(Template template, Scope scope, EvalContext ctx)
        {
            var sb = new StringBuilder();
            foreach (TemplateSegment segment in template.Segments)
            {
                sb.Append(segment.Kind == TemplateSegmentKind.Text ? segment.Text : Values.ToStr(Evaluate(segment.Node, scope, ctx)));
            }
            return sb.ToString();
        }

        /// <summary>The text of a call that reads it itself (@sql binds each { } as a parameter), as written; null for anything else.</summary>
        private static string TextAsWritten(ExprNode node, string callName) =>
            callName != null && TemplateParser.KeepsText(callName) && node is LiteralNode { Kind: LiteralKind.String } text ? text.Value : null;

        private (List<object>, Dictionary<string, object>) EvaluateArguments(IReadOnlyList<ArgumentNode> arguments, Scope scope, EvalContext ctx, string callName = null)
        {
            var args = new List<object>(arguments.Count);
            Dictionary<string, object> named = null;
            foreach (ArgumentNode a in arguments)
            {
                // A bare word (downloads, recursive, not recursive) means something only to the parameter it lands on.
                object value = TextAsWritten(a.Value, callName) ?? BareWordOf(a.Value, scope, ctx, afterNamed: named != null && a.Name == null) ?? BareListOf(a.Value, scope, ctx) ?? Evaluate(a.Value, scope, ctx);
                if (a.Name != null) (named ??= new Dictionary<string, object>(StringComparer.Ordinal))[a.Name] = value;
                else if (named != null && !(value is BareWord)) args.Add(new LatePositional(value));
                else args.Add(value);
            }
            return (args, named);
        }

        /// <summary>An array with bare words in it, <c>[large, small]</c>, for the binder to resolve against the parameter; null otherwise.</summary>
        private BareList BareListOf(ExprNode node, Scope scope, EvalContext ctx)
        {
            if (!(node is ListNode list) || !list.Items.Any(i => i is IdentifierNode)) return null;
            var words = list.Items.Select(i => i is IdentifierNode ? BareWordOf(i, scope, ctx, afterNamed: false) : null).ToList();
            if (words.All(w => w == null)) return null;
            return new BareList(list.Items.Select((item, n) => words[n] ?? Evaluate(item, scope, ctx)).ToList());
        }

        private BareWord BareWordOf(ExprNode node, Scope scope, EvalContext ctx, bool afterNamed)
        {
            // @kyle names a function when there is one; otherwise it can only be a handle, for a parameter that takes one.
            if (node is CommandCallNode { ArgumentList: null } reference && !CanResolve(reference, scope, ctx))
                return new BareWord("@" + reference.Name, false, afterNamed);
            // small-box and v1.2 read as words for a parameter whose type is read from text.
            if (node is BinaryNode && ctx.Origin != null && IsRawWord(node, "word", scope, ctx.Module)
                && node.DescendantsAndSelf().OfType<IdentifierNode>().All(i => !scope.TryGet(i.Name, out _))
                && node.Span.Start >= 0 && node.Span.Start + node.Span.Length <= ctx.Origin.Value.Length)
            {
                return new BareWord(ctx.Origin.Value.Substring(node.Span.Start, node.Span.Length).Trim(), false, afterNamed);
            }
            // posts.tags names a path of names, for a parameter that reads them: ->with(posts.tags).
            if ((node as MemberNode)?.DottedName is string path) return new BareWord(path, false, afterNamed);
            bool negated = false;
            if (node is UnaryNode { Operator: "not", Operand: IdentifierNode inner })
            {
                node = inner;
                negated = true;
            }
            if (node is IdentifierNode id && id.Name != "_" && !(ctx.Module != null && ctx.Module.Aliases.ContainsKey(id.Name))
                && !(!negated && scope.TryGet(id.Name, out object value) && value is IDeclaredType)
                && !(!negated && ErrorTypes.FindExact(id.Name, null) != null))
            {
                return new BareWord(id.Name, negated, afterNamed);
            }
            return null;
        }

        private bool CanResolve(CommandCallNode reference, Scope scope, EvalContext ctx)
        {
            try
            {
                ResolveCallable(reference.Name, scope, ctx, reference);
                return true;
            }
            catch (KScriptException)
            {
                return false;
            }
        }

        private object EvaluateRange(RangeNode range, Scope scope, EvalContext ctx)
        {
            object from = Evaluate(range.Start, scope, ctx), to = Evaluate(range.End, scope, ctx);
            object by = range.Step != null ? Evaluate(range.Step, scope, ctx) : null;
            // A range of a native type's values, or of dates by a duration, is declared by the type.
            try
            {
                if (NativeTypeRegistry.TryRange(from, to, by, out List<object> values)) return values;
            }
            catch (KScriptException ex) when (ex.Line == 0)
            {
                throw Fail(range, ctx, ex.Words, ex.Type);
            }
            double start = Values.ToNumber(from, "The start of a range");
            double end = Values.ToNumber(to, "The end of a range");
            double step = by != null ? Values.ToNumber(by, "The step of a range") : (end >= start ? 1 : -1);
            if (step == 0) throw Fail(range, ctx, "A range can't have a step of 0.", "Argument");
            var list = new List<object>();
            for (double x = start; step > 0 ? x < end : x > end; x += step)
            {
                list.Add(x);
                if (list.Count > 10_000_000) throw Fail(range, ctx, "The range is too large (more than 10 million numbers).", "Limit");
            }
            return list;
        }

        private object EvaluateBinary(BinaryNode bin, Scope scope, EvalContext ctx)
        {
            switch (bin.Operator)
            {
                case "and":
                    return Values.Truthy(Evaluate(bin.Left, scope, ctx)) && Values.Truthy(Evaluate(bin.Right, scope, ctx));
                case "or":
                    return Values.Truthy(Evaluate(bin.Left, scope, ctx)) || Values.Truthy(Evaluate(bin.Right, scope, ctx));
                case "??":
                {
                    object left;
                    try
                    {
                        left = Evaluate(bin.Left, scope, ctx);
                    }
                    catch (KScriptException ex) when ((ex.Type == "Name" || ex.Type == "Null") && (bin.Left is VariableNode || bin.Left is MemberNode))
                    {
                        // $maybe ?? 'default' works when $maybe was never declared.
                        left = null;
                    }
                    return left ?? Evaluate(bin.Right, scope, ctx);
                }
            }

            if (bin.Operator is "==" or "!=" or "===" or "!==")
            {
                // $user.handle == @kyle: next to a handle, an @name that isn't a function is a handle.
                if (bin.Right is CommandCallNode { ArgumentList: null } r && !CanResolve(r, scope, ctx))
                    return HandleCompare(bin, Evaluate(bin.Left, scope, ctx), r, ctx);
                if (bin.Left is CommandCallNode { ArgumentList: null } l && !CanResolve(l, scope, ctx))
                    return HandleCompare(bin, Evaluate(bin.Right, scope, ctx), l, ctx);
            }
            object a = Evaluate(bin.Left, scope, ctx);
            object b = Evaluate(bin.Right, scope, ctx);
            return Binary(bin.Operator, a, b, bin, ctx);
        }

        private object HandleCompare(BinaryNode bin, object other, CommandCallNode word, EvalContext ctx)
        {
            if (!(other is HandleName)) throw Fail(word, ctx, $"Unknown command @{word.Name}(). An @name without a function is a handle only next to a handle, such as $user.handle == @{word.Name}.", "Name");
            object handle = SemanticTypes.TryConvert(SemanticTypes.Handle, "@" + word.Name, out object h, out string reason) ? h : throw Fail(word, ctx, reason, "Type");
            return Binary(bin.Operator, other, handle, bin, ctx);
        }

        private object Binary(string op, object a, object b, ExprNode node, EvalContext ctx)
        {
            string where = Where(node, ctx);
            // An operator a native type declares (duration + duration, date - duration, ip in cidr) runs as it says.
            if (NativeOperators.BinaryOperators.Contains(op))
            {
                try
                {
                    if (NativeTypeRegistry.TryBinary(op, a, b, out object native)) return native;
                }
                catch (KScriptException ex) when (ex.Line == 0)
                {
                    throw Fail(node, ctx, ex.Words, ex.Type);
                }
                if (NativeTypeRegistry.BinaryProblem(op, a, b) is string problem) throw Fail(node, ctx, problem, "Type");
            }
            switch (op)
            {
                case "==": return Values.LooseEquals(a, b);
                case "!=": return !Values.LooseEquals(a, b);
                case "===": return Values.StrictEquals(a, b);
                case "!==": return !Values.StrictEquals(a, b);
                case "<": return Values.Compare(a, b, "lt") < 0;
                case "<=": return Values.Compare(a, b, "le") <= 0;
                case ">": return Values.Compare(a, b, "gt") > 0;
                case ">=": return Values.Compare(a, b, "ge") >= 0;
                case "in":
                    return b switch
                    {
                        List<object> list => list.Any(x => Values.LooseEquals(x, a)),
                        Dictionary<string, object> map => map.ContainsKey(Values.ToStr(a)),
                        string s => s.Contains(Values.ToStr(a), StringComparison.Ordinal),
                        null => false,
                        _ => throw Fail(node, ctx, $"'in' needs a list, map or text on the right, but got {Values.Describe(b)}{where}.", "Type"),
                    };
                case "+":
                    if (a is double x && b is double y) return x + y;
                    if (a is List<object> la && b is List<object> lb) return la.Concat(lb).ToList();
                    if (a is Dictionary<string, object> ma && b is Dictionary<string, object> mb)
                    {
                        var merged = new Dictionary<string, object>(ma);
                        foreach (var kv in mb) merged[kv.Key] = kv.Value;
                        return merged;
                    }
                    if (a is string || b is string) return Values.ToStr(a) + Values.ToStr(b);
                    if (a is DateTime date && b is double days) return date.AddDays(days);
                    return Values.ToNumber(a, $"'+'{where}") + Values.ToNumber(b, $"'+'{where}");
                case "-":
                    if (a is DateTime d1 && b is DateTime d2) return (d1 - d2).TotalDays;
                    if (a is DateTime d3 && b is double n) return d3.AddDays(-n);
                    return Values.ToNumber(a, $"'-'{where}") - Values.ToNumber(b, $"'-'{where}");
                case "*":
                    if (a is string rs && b is double times) return string.Concat(Enumerable.Repeat(rs, Math.Max(0, (int)times)));
                    return Values.ToNumber(a, $"'*'{where}") * Values.ToNumber(b, $"'*'{where}");
                case "/":
                {
                    double divisor = Values.ToNumber(b, $"'/'{where}");
                    if (divisor == 0) throw Fail(node, ctx, $"Division by zero{where}.", "Math");
                    return Values.ToNumber(a, $"'/'{where}") / divisor;
                }
                case "%":
                {
                    double divisor = Values.ToNumber(b, $"'%'{where}");
                    if (divisor == 0) throw Fail(node, ctx, $"Division by zero{where}.", "Math");
                    return Values.ToNumber(a, $"'%'{where}") % divisor;
                }
                case "**":
                    return Math.Pow(Values.ToNumber(a, $"'**'{where}"), Values.ToNumber(b, $"'**'{where}"));
            }
            throw Fail(node, ctx, $"Unknown operator '{op}'.", "Syntax");
        }

        #endregion

        #region Members and indexes

        private object GetMember(object target, string name, ExprNode node, EvalContext ctx)
        {
            switch (target)
            {
                case Record record:
                    try
                    {
                        if (record.Type.Tag is IRecordKind kind && kind.TryGetMember(this, record, name, out object loaded)) return loaded;
                        return record.Get(name);
                    }
                    catch (KScriptException ex)
                    {
                        throw Fail(node, ctx, ex.Words, ex.Type);
                    }
                case Dictionary<string, object> map:
                    return map.TryGetValue(name, out object v) ? v : null;
                case List<object> or string when name is "count" or "length":
                    // A list or text has no fields: the number of items or characters is the method, as for a map.
                    throw Fail(node, ctx, $".{name} isn't a field of {Values.Describe(target)}. The number of {(target is string ? "characters" : "items")} is ->count().", "Name");
                case ImportNamespace ns:
                    if (ns.TryGet(name, out object member))
                    {
                        return member switch
                        {
                            CommandDefinition cmd => new NativeFunction(cmd.Name, call => InvokeCommand(cmd, call)),
                            VariableDefinition variable => variable.Value(this, ctx.Module),
                            _ => member,
                        };
                    }
                    throw Fail(node, ctx, $"{ns.Display} has no '{name}'." + Suggest(name, ns.Names), "Name");
                case IKObject obj:
                    if (obj.TryGetMember(name, out object field)) return field;
                    break;
            }
            // A field a package gives the type: $response.status, $date.year.
            if (TryFindField(target, name, ctx.Module, out ValueFieldDefinition definition)) return definition.Handler(target, this);
            if (TryFindMethod(target, name, ctx.Module, out MethodDefinition asMethod) && asMethod.Receiver != "any")
                throw Fail(node, ctx, $"{name} is a method of {Values.Describe(target)}, not a field. Call it with ->{name}().", "Name");
            throw Fail(node, ctx, $"{Values.Describe(target)} has no field '{name}'." + (target is string || target is double ? $" To call a method, write ->{name}()." : string.Empty), "Name");
        }

        private object GetIndex(object target, object key, ExprNode node, EvalContext ctx)
        {
            switch (target)
            {
                case null:
                    throw Fail(node, ctx, $"Can't index into null{Where(node, ctx)}.", "Null");
                case List<object> list:
                {
                    int i = Values.ToInt(key, "A list index");
                    if (i < 0) i += list.Count;
                    return i >= 0 && i < list.Count ? list[i] : null;
                }
                case Dictionary<string, object> map:
                    return map.TryGetValue(Values.ToStr(key), out object v) ? v : null;
                case string s:
                {
                    int i = Values.ToInt(key, "A text index");
                    if (i < 0) i += s.Length;
                    return i >= 0 && i < s.Length ? s[i].ToString() : null;
                }
                case IKObject obj when obj.TryGetMember(Values.ToStr(key), out object field):
                    return field;
                // A field a package gives the type, by name: $file['size'].
                case IKObject when key is string name && TryFindField(target, name, ctx.Module, out ValueFieldDefinition definition):
                    return definition.Handler(target, this);
            }
            throw Fail(node, ctx, $"Can't index into {Values.Describe(target)}.", "Type");
        }

        #endregion

        #region Assignment

        internal void Assign(AssignmentNode node, Scope scope, EvalContext ctx, bool declare)
        {
            object value = Evaluate(node.Value, scope, ctx);
            ExprNode target = node.Target;

            if (target is VariableNode || target is IdentifierNode)
            {
                string name = node.Name;
                if (node.Operator != "=")
                {
                    if (!scope.TryGet(name, out object current)) throw Fail(target, ctx, $"${name} is not defined, so '{node.Operator}' can't update it.", "Name");
                    value = Combine(current, node.Operator, value, node, ctx);
                    scope.Assign(name, value);
                    return;
                }
                if (declare) scope.Declare(name, value);
                else scope.Assign(name, value);
                return;
            }

            if (scope.IsConst(node.Name)) throw Fail(target, ctx, $"${node.Name} is const, so it can't be changed.", "Const");

            switch (target)
            {
                case MemberNode member:
                {
                    object container = Evaluate(AssignedThrough(member.Target), scope, ctx);
                    if (container is not Dictionary<string, object> map)
                        throw Fail(member, ctx, $"Only map fields can be set, but {ReadSourceOf(member.Target, ctx)} is {Values.Describe(container)}.", "Type");
                    if (node.Operator != "=") value = Combine(map.TryGetValue(member.Name, out object cur) ? cur : null, node.Operator, value, node, ctx);
                    SetKey(map, member.Name, value, member, ctx);
                    return;
                }
                case IndexNode index:
                {
                    object container = Evaluate(AssignedThrough(index.Target), scope, ctx);
                    object key = Evaluate(index.Index, scope, ctx);
                    switch (container)
                    {
                        case Dictionary<string, object> map:
                        {
                            string k = Values.ToStr(key);
                            if (node.Operator != "=") value = Combine(map.TryGetValue(k, out object cur) ? cur : null, node.Operator, value, node, ctx);
                            SetKey(map, k, value, index, ctx);
                            return;
                        }
                        case List<object> list:
                        {
                            int i = Values.ToInt(key, "A list index");
                            if (i < 0) i += list.Count;
                            if (i == list.Count && node.Operator == "=") { list.Add(value); return; }
                            if (i < 0 || i >= list.Count) throw Fail(index, ctx, $"Index {Values.ToStr(key)} is outside the list (it has {list.Count} items).", "Index");
                            if (node.Operator != "=") value = Combine(list[i], node.Operator, value, node, ctx);
                            list[i] = value;
                            return;
                        }
                    }
                    throw Fail(index, ctx, $"Only list items and map keys can be set, but {ReadSourceOf(index.Target, ctx)} is {Values.Describe(container)}.", "Type");
                }
            }
            throw Fail(target, ctx, "Only a variable, a map field or a list item can be assigned to.", "Syntax");
        }

        /// <summary>How an assignment's target is read, for a message: <c>s.a = 1</c> sets a field of <c>$s</c>.</summary>
        private string ReadSourceOf(ExprNode node, EvalContext ctx)
        {
            ExprNode root = node;
            while (root is MemberNode or IndexNode) root = root is MemberNode m ? m.Target : ((IndexNode)root).Target;
            return (root is IdentifierNode ? "$" : string.Empty) + SourceOf(node, ctx);
        }

        /// <summary>
        /// What a field or item is set on, as an expression to read: the name an assignment starts with is written
        /// bare (<c>user.name = 'Ada'</c>, <c>totals[0] += 1</c>), and it is the variable.
        /// </summary>
        private static ExprNode AssignedThrough(ExprNode node) => node switch
        {
            IdentifierNode id => new VariableNode { Name = id.Name, Span = id.Span },
            MemberNode m when AssignedThrough(m.Target) is ExprNode inner && !ReferenceEquals(inner, m.Target) =>
                new MemberNode { Target = inner, Name = m.Name, NameSpan = m.NameSpan, NullSafe = m.NullSafe, Span = m.Span },
            IndexNode i when AssignedThrough(i.Target) is ExprNode inner && !ReferenceEquals(inner, i.Target) =>
                new IndexNode { Target = inner, Index = i.Index, Span = i.Span },
            _ => node,
        };

        /// <summary>Sets a map key. A record checks the key is one of its fields and converts the value.</summary>
        private void SetKey(Dictionary<string, object> map, string key, object value, ExprNode node, EvalContext ctx)
        {
            if (map is not Record record)
            {
                map[key] = value;
                return;
            }
            try
            {
                record.Set(this, key, value);
            }
            catch (KScriptException ex)
            {
                throw Fail(node, ctx, ex.Words, ex.Type);
            }
        }

        internal object Combine(object current, string op, object value, ExprNode node, EvalContext ctx)
        {
            switch (op)
            {
                case "??=": return current ?? value;
                case "+=":
                    if (current is List<object> list && value is not List<object>) return list.Append(value).ToList();
                    return Binary("+", current, value, node, ctx);
                case "-=": return Binary("-", current, value, node, ctx);
                case "*=": return Binary("*", current, value, node, ctx);
                case "/=": return Binary("/", current, value, node, ctx);
                case "=": return value;
            }
            throw Fail(node, ctx, $"Unknown assignment operator '{op}'.", "Syntax");
        }

        /// <summary>Combines a value with an operator, as op="+=" on &lt;set&gt; does.</summary>
        public object Combine(object current, string op, object value) => Combine(current, op, value, null, default);

        #endregion

        #region Calls

        private static string Suggest(string name, IEnumerable<string> names)
        {
            string close = Values.Closest(name, names);
            return close != null ? $" Did you mean {close}?" : string.Empty;
        }

        /// <summary>Finds what <c>@name</c> refers to: a function in scope, an imported script's export, or a command.</summary>
        private object ResolveCallable(string name, Scope scope, EvalContext ctx, ExprNode node)
        {
            Module module = ctx.Module;
            int dot = name.IndexOf('.');
            if (dot > 0)
            {
                string alias = name.Substring(0, dot), member = name.Substring(dot + 1);
                if (module == null || !module.Aliases.TryGetValue(alias, out ImportNamespace ns))
                {
                    string close = module == null ? null : Values.Closest(alias, module.Aliases.Keys);
                    throw Fail(node, ctx, Wording.Of($"@{name}: nothing is imported as '{alias}'.") + (close != null ? $" Did you mean {close}?" : Wording.Of($" Import it with <import package=\"...\" as=\"{alias}\" /> or <import path=\"...\" as=\"{alias}\" />.")), "Name");
                }
                if (ns.TryGet(member, out object found))
                {
                    if (found is VariableDefinition v) return v.Value(this, module);
                    return found;
                }
                throw Fail(node, ctx, $"{ns.Display} has no command '{member}'." + Suggest(member, ns.Names), "Name");
            }

            if (scope.TryGet(name, out object stored) && stored is ICallable) return stored;
            if (module != null && module.Commands.TryGetValue(name, out CommandDefinition command)) return command;

            if (Retired.Commands.TryGetValue(name, out var retired))
                throw Fail(node, ctx, $"@{name}() was removed in KScript 2. {Wording.Prose(retired.Replacement)}", "Retired");

            KScriptPackage owner = Packages.FindCommand(name, module?.Packages).FirstOrDefault();
            if (owner != null)
                throw Fail(node, ctx, $"@{name}() is part of {owner.Name}. Add {ImportHint(module, owner.Name)} at the top of the script.", "Import");

            IEnumerable<string> known = (module?.Commands.Keys ?? Enumerable.Empty<string>()).Concat(scope.Names().Where(n => scope.TryGet(n, out object f) && f is ICallable));
            throw Fail(node, ctx, $"Unknown command @{name}()." + Suggest(name, known).Replace("Did you mean ", "Did you mean @"), "Name");
        }

        private object CallCommand(string name, List<object> args, Dictionary<string, object> named, Scope scope, EvalContext ctx, ExprNode node, CallSite site = null)
        {
            object target = ResolveCallable(name, scope, ctx, node);
            return CallResolved(target, name, args, named, scope, ctx, node, site);
        }

        private object CallResolved(object target, string name, List<object> args, Dictionary<string, object> named, Scope scope, EvalContext ctx, ExprNode node, CallSite site = null)
        {
            switch (target)
            {
                case CommandDefinition command:
                    return InvokeCommand(command, new CallContext(this, command.Name, args, named, scope, ctx.Module) { Site = site });
                case ICallable fn:
                    return Call(fn, args, named, scope, ctx.Module);
            }
            throw Fail(node, ctx, $"{name} is {Values.Describe(target)}, not a function.", "Type");
        }

        internal object InvokeCommand(CommandDefinition command, CallContext call)
        {
            CallSite site = call.Site;
            call = ArgumentBinder.Bind(command.Info, call);
            call.Site = site;
            CallableInfoCheck(command.Info, call);
            return command.Handler(call);
        }

        private static void CallableInfoCheck(Metadata.CallableInfo info, CallContext call)
        {
            int count = call.Args.Count - call.Skipped.Count + call.Named.Count;
            if (count < info.MinArgs)
                throw new KScriptException($"{call.Display} needs {(info.MinArgs == info.MaxArgs ? string.Empty : "at least ")}{info.MinArgs} argument{(info.MinArgs == 1 ? string.Empty : "s")}: {info.Labels.First()}.", "Argument");
            if (call.Args.Count > info.MaxArgs)
                throw new KScriptException($"{call.Display} takes at most {info.MaxArgs} argument{(info.MaxArgs == 1 ? string.Empty : "s")}: {info.Labels.First()}.", "Argument");
        }

        private object EvaluateCall(CallNode call, object pipedValue, Scope scope, EvalContext ctx)
        {
            (List<object> args, Dictionary<string, object> named) = EvaluateArguments(call.Arguments, scope, ctx, (call.Callee as IdentifierNode)?.Name);
            bool piped = pipedValue != NotPiped;
            if (piped) args.Insert(0, pipedValue);

            switch (call.Callee)
            {
                case IdentifierNode id:
                {
                    // filter(...) after a pipe is a method of the value; slugify() is a function or command.
                    if (scope.TryGet(id.Name, out object stored) && stored is ICallable fn) return Call(fn, args, named, scope, ctx.Module);
                    // NotFound(id: 42) makes an error, for throw.
                    if (!piped && ErrorTypes.FindExact(id.Name, scope) is ErrorType error)
                    {
                        try
                        {
                            return error.Create(this, args, named).ToValue();
                        }
                        catch (KScriptException ex)
                        {
                            throw Fail(call, ctx, ex.Words, ex.Type);
                        }
                    }
                    // Repo($data), Repo(name: 'x'), Email('a@b.co'), Size('small'): calling a type makes a value of it.
                    if (stored is RecordType or RefinedType or EnumType)
                    {
                        return Construct(stored, args, named, scope, ctx);
                    }
                    if (piped && Extensions.TryFind(pipedValue, id.Name, ctx.Module, out ExtensionMethod extension))
                    {
                        return Extensions.Call(this, extension, pipedValue, args.Skip(1).ToList(), named, scope, ctx.Module);
                    }
                    if (piped && TryFindMethod(pipedValue, id.Name, ctx.Module, out MethodDefinition method))
                    {
                        return InvokeMethodDefinition(method, pipedValue, args.Skip(1).ToList(), named, scope, ctx);
                    }
                    if (piped && pipedValue is IKObject obj
                        && obj.TryCallMethod(ArgumentBinder.Plain(new CallContext(this, id.Name, args.Skip(1).ToList(), named, scope, ctx.Module, isMethod: true)), id.Name, out object objResult))
                    {
                        return objResult;
                    }
                    if (ctx.Module != null && ctx.Module.Commands.TryGetValue(id.Name, out CommandDefinition command))
                    {
                        return InvokeCommand(command, new CallContext(this, command.Name, args, named, scope, ctx.Module) { Site = piped ? null : new CallSite(call, call.Arguments, ctx) });
                    }
                    if (piped) return InvokeMethod(pipedValue, id.Name, args.Skip(1).ToList(), named, scope, ctx, call);
                    return CallCommand(id.Name, args, named, scope, ctx, call.Callee, new CallSite(call, call.Arguments, ctx));
                }

                case MemberNode member when member.Target is IdentifierNode alias && ctx.Module != null && ctx.Module.Aliases.ContainsKey(alias.Name):
                    return CallCommand(alias.Name + "." + member.Name, args, named, scope, ctx, call.Callee, new CallSite(call, call.Arguments, ctx));
            }

            object callee = Evaluate(call.Callee, scope, ctx);
            return CallResolved(callee, SourceOf(call.Callee, ctx), args, named, scope, ctx, call.Callee);
        }

        private static readonly object NotPiped = new object();

        /// <summary>Calling a declared type: a record of a type or model, a checked value of a refined type or an enum.</summary>
        private object Construct(object type, List<object> args, Dictionary<string, object> named, Scope scope, EvalContext ctx)
        {
            named ??= new Dictionary<string, object>();
            switch (type)
            {
                case RecordType record:
                    return record.Construct(this, args, named);
                case RefinedType refined:
                    if (args.Count != 1 || named.Count > 0) throw new KScriptException($"{refined.Name}() takes one value to check: {refined.Name}($value).", "Argument");
                    return Shapes.Convert(this, args[0], TypeRef.Of(refined), "The value", refined.Closure, refined.Module);
                case EnumType e:
                    if (args.Count != 1 || named.Count > 0) throw new KScriptException($"{e.Name}() takes one value to check: {e.Name}($value).", "Argument");
                    return e.Check(args[0], "The value");
                default:
                    throw new KScriptException($"{Values.Describe(type)} can't be called.", "Type");
            }
        }

        private object EvaluatePipe(PipeNode pipe, Scope scope, EvalContext ctx)
        {
            // "select ... {$id}" |> @sql(): the text reaches @sql as written, like an argument does.
            string piped = pipe.Right switch
            {
                CommandCallNode c => c.Name,
                CallNode { Callee: IdentifierNode id } => id.Name,
                _ => null,
            };
            object left = TextAsWritten(pipe.Left, piped) ?? Evaluate(pipe.Left, scope, ctx);
            switch (pipe.Right)
            {
                case CommandCallNode command:
                {
                    (List<object> args, Dictionary<string, object> named) = EvaluateArguments(command.Arguments, scope, ctx, command.Name);
                    args.Insert(0, left);
                    return CallCommand(command.Name, args, named, scope, ctx, command);
                }
                case CallNode call:
                    return EvaluateCall(call, left, scope, ctx);
                case MethodCallNode:
                    throw Fail(pipe.Right, ctx, "After |> write the method name without ->: $list |> filter(fn(x) => ...).", "Syntax");
            }
            object right = Evaluate(pipe.Right, scope, ctx);
            return CallResolved(right, SourceOf(pipe.Right, ctx), new List<object> { left }, null, scope, ctx, pipe.Right);
        }

        #endregion

        #region Methods

        /// <summary>The field a package adds to values like <paramref name="target"/>: <c>$response.status</c>.</summary>
        private bool TryFindField(object target, string name, Module module, out ValueFieldDefinition field)
        {
            field = null;
            if (target == null || target is Dictionary<string, object>) return false;
            string type = Values.TypeOf(target);
            if (module != null && module.Fields.TryGetValue(type + ":" + name, out field)) return true;
            // A value can reach a script that doesn't import the package it came from.
            field = Packages.FindField(type, name);
            return field != null;
        }

        private bool TryFindMethod(object target, string name, Module module, out MethodDefinition method)
        {
            method = null;
            if (module == null) return false;
            string type = Values.TypeOf(target);
            if (target is ErrorValue && module.Methods.TryGetValue("error:" + name, out method)) return true;
            // A package's records (a model's) have its methods as well as maps'.
            if (target is Record record && record.Type.RecordKind != null && module.Methods.TryGetValue(record.Type.RecordKind + ":" + name, out method)) return true;
            // A list of one native type has that type's list methods too: $versions->without(prereleases).
            if (target is List<object> { Count: > 0 } items && items[0] is IKObject first && NativeTypeRegistry.Get(first.TypeName) != null
                && items.All(x => x is IKObject k && k.TypeName == first.TypeName) && module.Methods.TryGetValue(first.TypeName + "[]:" + name, out method)) return true;
            if (module.Methods.TryGetValue(type + ":" + name, out method)) return true;
            if (target is string s && Values.TryNumber(s, out _) && module.Methods.TryGetValue("number:" + name, out method)) return true;
            // An email, handle, uuid or regex is text too, so the string methods that make sense for it work on it.
            if (target is ISemanticText semantic && KType.AcceptsMethod(KType.String, name, semantic.TypeName) && module.Methods.TryGetValue("string:" + name, out method)) return true;
            return module.Methods.TryGetValue("any:" + name, out method);
        }

        private object InvokeMethodDefinition(MethodDefinition method, object target, List<object> args, Dictionary<string, object> named, Scope scope, EvalContext ctx)
        {
            var call = ArgumentBinder.Bind(method.Info, new CallContext(this, method.Name, args, named, scope, ctx.Module, isMethod: true));
            CallableInfoCheck(method.Info, call);
            if (method.Receiver == "number" && target is string s) target = Values.ToNumber(s, call.Display);
            if (method.Receiver == "string" && target is ISemanticText text) target = text.Text;
            return method.Handler(target, call);
        }

        /// <summary>A regex, email or UUID written without quotes. One that isn't valid fails at the literal.</summary>
        private object TypedLiteral(LiteralNode lit, EvalContext ctx)
        {
            string type = lit.Kind switch { LiteralKind.Regex => SemanticTypes.Regex, LiteralKind.Email => SemanticTypes.Email, LiteralKind.Url => "url", LiteralKind.Duration => Duration.TypeName, _ => SemanticTypes.Uuid };
            if (SemanticTypes.TryConvert(type, lit.Value, out object value, out string reason)) return value;
            throw Fail(lit, ctx, $"{lit.Value} isn't {KScript.Metadata.NativeTypes.Get(type).WithArticle}: {reason}.", "Syntax");
        }

        /// <summary>Calls <c>value->name(args)</c>.</summary>
        public object InvokeMethod(object target, string name, List<object> args, Dictionary<string, object> named, Scope scope, EvalContext ctx, ExprNode node)
        {
            // extend date { fn json() => ... }: methods the script (its imports, its project) adds to the type come first.
            if (Extensions.TryFind(target, name, ctx.Module, out ExtensionMethod extension))
            {
                return Extensions.Call(this, extension, target, args, named, scope, ctx.Module);
            }

            // A <type>'s own methods: fn inside the type, with the record's fields (and $this) as variables.
            if (target is Record record && record.Type.OwnMethods.TryGetValue(name, out MarkupElement methodElement))
            {
                Scope methodScope = record.Type.Closure.Child();
                foreach (KeyValuePair<string, object> field in record) methodScope.Declare(field.Key, field.Value);
                methodScope.Declare("this", record);
                DeclareFunction(methodElement, methodScope, record.Type.Module);
                methodScope.TryGet(name, out object fn);
                return Call((ICallable)fn, args, named, scope, ctx.Module);
            }

            // Methods the record's kind adds, such as a model record's relations: $user->posts().
            if (target is Record kindRecord && kindRecord.Type.Tag is IRecordKind kind && kind.HasMethod(kindRecord, name)
                && kind.TryCallMethod(ArgumentBinder.Plain(new CallContext(this, name, args, named, scope, ctx.Module, isMethod: true)), kindRecord, name, out object added))
            {
                return added;
            }

            // A package's methods for its own objects come first, so their arguments are bound by name (and flags) like commands'.
            bool defined = TryFindMethod(target, name, ctx.Module, out MethodDefinition method);
            // A renamed method says so before its arguments are looked at: User->avg(age) is told about ->average(), not about age.
            if (!defined && target != null && RetiredMembers.Method(Values.TypeOf(target), name) is KScript.Metadata.RetiredInfo retiredMethod)
                throw Fail(node, ctx, $"->{name}() is no longer part of KScript. {retiredMethod.Replacement}", "Name");
            if (defined && target is IKObject && method.Receiver != "any") return InvokeMethodDefinition(method, target, args, named, scope, ctx);
            // A NativeText has no methods of its own, so an any-method (->is_a(semver)) binds its bare words as declared.
            if (defined && target is NativeText) return InvokeMethodDefinition(method, target, args, named, scope, ctx);
            if (target is IKObject obj && obj.TryCallMethod(ArgumentBinder.Plain(new CallContext(this, name, args, named, scope, ctx.Module, isMethod: true)), name, out object result))
            {
                return result;
            }
            if (defined)
            {
                return InvokeMethodDefinition(method, target, args, named, scope, ctx);
            }

            if (TryFindField(target, name, ctx.Module, out _))
                throw Fail(node, ctx, $"{name} is a field of {Values.Describe(target)}, not a method. Read it with a dot and no brackets: .{name}", "Name");

            if (target == null)
                throw Fail(node, ctx, $"Can't call ->{name}() on null. Use ?? to give a default, such as ($value ?? '')->{name}().", "Null");

            if (target is Record typed && !(ctx.Module?.Methods.Values.Any(m => m.Name == name && m.Receiver == "map") ?? false))
            {
                string fields = typed.Type.OwnMethods.Count > 0 ? Suggest(name, typed.Type.OwnMethods.Keys).Replace("Did you mean ", "Did you mean ->").Replace("?", "()?") : string.Empty;
                throw Fail(node, ctx, $"A {typed.Type.Name} has no method ->{name}().{fields} Methods are added with fn inside the type.", "Name");
            }

            string type = Values.TypeOf(target);
            KScriptPackage owner = Packages.FindMethod(name, ctx.Module?.Packages).FirstOrDefault(p => p.Methods.Any(m => m.Name == name && (m.Receiver == type || m.Receiver == "any")));
            if (owner != null)
                throw Fail(node, ctx, $"->{name}() on a {type} is part of {owner.Name}. Add {ImportHint(ctx.Module, owner.Name)} at the top of the script.", "Import");

            IEnumerable<string> known = ctx.Module?.Methods.Values.Where(m => m.Receiver == type || m.Receiver == "any").Select(m => m.Name) ?? Enumerable.Empty<string>();
            string noun = type switch { "list" => "Lists", "map" => "Maps", "string" => "Text values", "number" => "Numbers", "date" => "Dates", "bool" => "Bools", "fn" => "Functions", _ => $"Values of type {type}" };
            string hint = target is Dictionary<string, object> ? $" To read a key, write .{name} or ['{name}']."
                : !(ctx.Module?.Methods.ContainsKey("string:" + name) ?? false) ? string.Empty
                : target is ISemanticText ? $" Use ->to_string()->{name}() to work on its text."
                : !(target is DateTime or double or bool || type == KType.File) ? string.Empty
                // ->json() reads JSON text; a date or number is written as JSON with ->to_json().
                : string.Equals(name, "json", StringComparison.OrdinalIgnoreCase) ? " To write it as JSON text, use ->to_json()."
                : $" Use ->to_string()->{name}() to work on its text.";
            throw Fail(node, ctx, $"{noun} have no method ->{name}()." + Suggest(name, known).Replace("Did you mean ", "Did you mean ->").Replace("?", "()?") + hint, "Name");
        }

        #endregion

        private static string ImportHint(Module module, string package) =>
            module?.ImportHint(package) ?? $"<import package=\"{package}\" />";
    }
}
