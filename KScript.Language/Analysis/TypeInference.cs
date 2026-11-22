using KScript.Metadata;
using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>Works out the shape of an expression's value from the source (literals, declarations, return types).</summary>
    public sealed class TypeInference
    {
        private static readonly HashSet<string> SameShapeListMethods = new HashSet<string> { "filter", "reject", "each", "sort", "sort_by", "reverse", "slice", "unique", "push", "take", "skip" };
        private static readonly HashSet<string> ItemListMethods = new HashSet<string> { "first", "last", "find", "max_by", "min_by", "pop" };

        private readonly LanguageCatalog catalog;
        private readonly DocumentAnalysis result;

        public TypeInference(LanguageCatalog catalog, DocumentAnalysis analysis)
        {
            this.catalog = catalog ?? LanguageCatalog.Default;
            result = analysis;
        }

        private static TextSpan VariableNameSpan(VariableNode v, ExpressionRegion region)
        {
            TextSpan span = region.Source.ToSource(v.Span);
            return TextSpan.FromBounds(Math.Min(span.Start + 1, span.End), span.End);
        }

        /// <summary>The shape of an expression's value, as far as it can be told from the source.</summary>
        public TypeShape Infer(ExprNode node, ExpressionRegion region)
        {
            switch (node)
            {
                case LiteralNode lit:
                    return new TypeShape(lit.Kind switch
                    {
                        LiteralKind.Number => KType.Number,
                        LiteralKind.String => KType.String,
                        LiteralKind.Bool => KType.Bool,
                        LiteralKind.Regex => KType.Regex,
                        LiteralKind.Email => KType.Email,
                        LiteralKind.Uuid => KType.Uuid,
                        LiteralKind.Url => "url",
                        LiteralKind.Duration => "duration",
                        _ => KType.Null,
                    });

                case ListNode list:
                {
                    TypeShape shape = new TypeShape(KType.List);
                    if (list.Items.Count > 0)
                    {
                        TypeShape first = Infer(list.Items[0], region);
                        if (first.Type == KType.Map)
                        {
                            TypeShape merged = new TypeShape(KType.Map);
                            foreach (ExprNode item in list.Items)
                            {
                                foreach (KeyValuePair<string, FieldInfo> f in Infer(item, region).Fields)
                                {
                                    merged.Fields.TryAdd(f.Key, f.Value);
                                }
                            }
                            shape.Element = merged;
                        }
                        else
                        {
                            shape.Element = list.Items.All(i => Infer(i, region).Type == first.Type) ? first : TypeShape.Unknown;
                        }
                    }
                    return shape;
                }

                case MapNode map:
                {
                    TypeShape shape = new TypeShape(KType.Map);
                    foreach (MapEntryNode entry in map.Entries)
                    {
                        shape.Fields.TryAdd(entry.Key, new FieldInfo(entry.Key, Infer(entry.Value, region), region.Source.ToSource(entry.KeySpan)));
                    }
                    return shape;
                }

                case RangeNode _:
                    return new TypeShape(KType.List) { Element = new TypeShape(KType.Number) };

                case LambdaNode _:
                    return new TypeShape(KType.Fn);

                case VariableNode v:
                {
                    TextSpan span = VariableNameSpan(v, region);
                    return result.RootScope.Innermost(span.Start).Lookup(v.Name, span.Start)?.Shape ?? TypeShape.Unknown;
                }

                case CallNode call when call.Callee is IdentifierNode errorName && ErrorTypeNamed(errorName, region) is TypeShape errorType:
                    // NotFound(id: 42) makes an error of that type.
                    return errorType.Instance ?? Analyzer.BaseErrorShape();

                case CallNode call when call.Callee is IdentifierNode jobName && JobTypeNamed(jobName, region) is TypeShape jobType:
                    // SendWelcome(user_id: 7) makes a job.
                    return jobType.Instance ?? new TypeShape(KType.Job) { Name = jobName.Name };

                case IdentifierNode id when ErrorTypeNamed(id, region) is TypeShape errorType:
                    return errorType;

                case MemberNode member:
                {
                    TypeShape target = Infer(member.Target, region);
                    if (target.Fields.TryGetValue(member.Name, out FieldInfo f)) return f.Shape;
                    // A field the value's type has: $response.status, $date.year.
                    return (catalog.GetField(target.Kind, member.Name) ?? catalog.GetField(target.Type, member.Name)) is ValueFieldInfo builtIn && builtIn.Type is not (null or KType.Any)
                        ? new TypeShape(builtIn.Type) { Declared = true }
                        : TypeShape.Unknown;
                }

                case IndexNode index:
                {
                    TypeShape target = Infer(index.Target, region);
                    if (target.Type == KType.List)
                    {
                        return target.Element ?? TypeShape.Unknown;
                    }
                    if (target.Type == KType.Map && index.Index is LiteralNode key && target.Fields.TryGetValue(key.Value, out FieldInfo f))
                    {
                        return f.Shape;
                    }
                    return TypeShape.Unknown;
                }

                case CommandCallNode c:
                {
                    Symbol fn = FunctionNamed(c.Name);
                    if (fn != null)
                    {
                        return fn.ReturnShape;
                    }
                    CallableInfo command = catalog.GetCommand(c.Name);
                    if (command == null)
                    {
                        return TypeShape.Unknown;
                    }
                    TypeShape json = null;
                    if (command.Name == "read_json" && c.Arguments.Count > 0 && c.Arguments[0].Value is LiteralNode { Kind: LiteralKind.String } file)
                    {
                        json = result.JsonFileShape(file.Value);
                    }
                    if (MappedShape(command, c.Arguments, 0, json, region) is TypeShape mapped)
                    {
                        return mapped;
                    }
                    if (json != null)
                    {
                        return json;
                    }
                    if (command.Package == "KScript.Data" && command.Name == "db")
                    {
                        // @db('users') is a query on the table; @db() and @db(connection: reports) are the database.
                        return new TypeShape(c.Arguments.Any(a => a.Name == null || a.Name == "table") ? KType.Query : KType.Database);
                    }
                    TypeShape shape = new TypeShape(command.ReturnType);
                    if (command.Name == "files")
                    {
                        shape.Element = new TypeShape(KType.File);
                    }
                    return shape;
                }

                case MethodCallNode m:
                {
                    TypeShape receiver = Infer(m.Target, region);
                    // ->map(fn(p) => $p.name) is a list of whatever the lambda returns.
                    if (receiver.Type == KType.List && m.Name == "map" && m.Arguments.Count == 1 && m.Arguments[0].Value is LambdaNode lambda)
                    {
                        TypeShape item = Infer(lambda.Body, region);
                        return item.Type == KType.Any ? new TypeShape(KType.List) : new TypeShape(KType.List) { Element = item };
                    }
                    if (receiver.Type == KType.Type && receiver.Instance != null && m.Name == "from")
                    {
                        TypeShape data = m.Arguments.Count > 0 ? Infer(m.Arguments[0].Value, region) : TypeShape.Unknown;
                        return data.Type == KType.List ? new TypeShape(KType.List) { Element = receiver.Instance } : receiver.Instance;
                    }
                    if (m.Name == "json" && MappedShape(catalog.GetMethods("json", receiver.Type).FirstOrDefault(x => KType.AcceptsMethod(x.ReceiverType, x.Name, receiver.Type)), m.Arguments, 0, null, region) is TypeShape asType)
                    {
                        return asType;
                    }
                    return InferMethod(m.Name, receiver, m.Arguments.Count);
                }

                case PipeNode pipe:
                {
                    TypeShape left = Infer(pipe.Left, region);
                    switch (pipe.Right)
                    {
                        case CallNode call when call.Callee is IdentifierNode id:
                            if (FunctionNamed(id.Name) is Symbol piped)
                            {
                                return piped.ReturnShape;
                            }
                            // $text |> json(as: Repo) gives records, as $text->json(as: Repo) does.
                            if (id.Name == "json" && MappedShape(catalog.GetMethods("json", left.Type).FirstOrDefault(x => KType.AcceptsMethod(x.ReceiverType, x.Name, left.Type)), call.Arguments, 0, null, region) is TypeShape asPiped)
                            {
                                return asPiped;
                            }
                            return InferMethod(id.Name, left, call.Arguments.Count);
                        case CommandCallNode command:
                            return Infer(command, region);
                        default:
                            return TypeShape.Unknown;
                    }
                }

                case UnaryNode unary:
                    return unary.Operator == "not" ? new TypeShape(KType.Bool) : new TypeShape(KType.Number);

                case BinaryNode binary:
                    switch (binary.Operator)
                    {
                        case "==": case "!=": case "===": case "!==": case "<": case "<=": case ">": case ">=":
                        case "in": case "and": case "or":
                            return new TypeShape(KType.Bool);
                        case "??":
                        {
                            TypeShape l = Infer(binary.Left, region);
                            return l.Type == KType.Any || l.Type == KType.Null ? Infer(binary.Right, region) : l;
                        }
                        case "+":
                        {
                            TypeShape l = Infer(binary.Left, region);
                            TypeShape r = Infer(binary.Right, region);
                            if (l.Type == KType.String || r.Type == KType.String)
                            {
                                return new TypeShape(KType.String);
                            }
                            if (l.Type == KType.List)
                            {
                                return l;
                            }
                            return l.Type == KType.Number && r.Type == KType.Number ? new TypeShape(KType.Number) : TypeShape.Unknown;
                        }
                        case "*" when Infer(binary.Left, region).Type == KType.String:
                            // 'ab' * 3 repeats the text.
                            return new TypeShape(KType.String);
                        default:
                            return new TypeShape(KType.Number);
                    }

                case MatchNode match:
                    return Analyzer.Unify(match.Arms.Select(a => Infer(a.Result, region)));

                case ConditionalNode cond:
                {
                    TypeShape a = Infer(cond.WhenTrue, region);
                    TypeShape b = Infer(cond.WhenFalse, region);
                    return a.Type == b.Type ? a : TypeShape.Unknown;
                }

                default:
                    return TypeShape.Unknown;
            }
        }

        /// <summary>The job type a name means here: a declared job (or null).</summary>
        private TypeShape JobTypeNamed(IdentifierNode id, ExpressionRegion region)
        {
            int offset = region.Source.ToSource(id.Span.Start);
            Symbol symbol = result.RootScope.Innermost(offset).Lookup(id.Name, offset);
            return symbol?.Shape.Type == KType.JobType ? symbol.Shape : null;
        }

        /// <summary>The error type a bare name means here: a declared &lt;error&gt;, or a built-in such as Validation.</summary>
        private TypeShape ErrorTypeNamed(IdentifierNode id, ExpressionRegion region)
        {
            int offset = region.Source.ToSource(id.Span.Start);
            Symbol symbol = result.RootScope.Innermost(offset).Lookup(id.Name, offset);
            if (symbol?.Shape.Type == KType.ErrorType)
            {
                return symbol.Shape;
            }
            return symbol == null && KScript.Runtime.ErrorTypes.BuiltIn.TryGetValue(id.Name, out KScript.Runtime.ErrorType b) && b.Name == id.Name
                ? new TypeShape(KType.ErrorType) { Name = b.Name } : null;
        }

        /// <summary>
        /// The result of a call with an <c>as:</c> argument (<c>->json(as: Repo)</c>): records of that type, or a list
        /// of them when the type says so or the data is known to be a list. Null when the call has no type to map to.
        /// </summary>
        private TypeShape MappedShape(CallableInfo callable, IReadOnlyList<ArgumentNode> args, int piped, TypeShape data, ExpressionRegion region)
        {
            int index = callable?.Signatures.FirstOrDefault()?.Parameters.ToList().FindIndex(p => p.Name == "as" && p.Type == KType.Type) ?? -1;
            if (index < 0)
            {
                return null;
            }
            ArgumentNode arg = args.FirstOrDefault(a => a.Name == "as") ?? (args.Count > index - piped && index - piped >= 0 && args[index - piped].Name == null ? args[index - piped] : null);
            if (arg == null)
            {
                return null;
            }
            int offset = region.Source.ToSource(arg.Value.Span.Start);
            TypeShape shape = arg.Value switch
            {
                IdentifierNode id => TypeNamed(id.Name, offset),
                LiteralNode { Kind: LiteralKind.String } text => TypeNamed(text.Value, offset),
                _ => Infer(arg.Value, region) is { Type: KType.Type, Instance: TypeShape instance } ? instance : null,
            };
            if (shape != null && shape.Type == KType.Map && data?.Type == KType.List)
            {
                return new TypeShape(KType.List) { Element = shape };
            }
            return shape;
        }

        /// <summary>
        /// The shape a type written in the source stands for: <c>string</c>, <c>int</c>, <c>Repo</c>, <c>Repo[]</c>,
        /// <c>string?</c>. An &lt;enum&gt; is a string. Null when the type isn't known at <paramref name="offset"/>.
        /// </summary>
        public TypeShape TypeNamed(string text, int offset)
        {
            string t = WithoutTypeArguments((text ?? string.Empty).Trim());
            if (t.EndsWith("?", StringComparison.Ordinal))
            {
                t = t.Substring(0, t.Length - 1).TrimEnd();
            }
            int depth = 0;
            while (t.EndsWith("[]", StringComparison.Ordinal))
            {
                depth++;
                t = t.Substring(0, t.Length - 2).TrimEnd();
            }
            TypeShape shape = t.ToLowerInvariant() switch
            {
                "int" or "float" or "number" => new TypeShape(KType.Number) { Declared = depth == 0 },
                "string" => new TypeShape(KType.String) { Declared = depth == 0 },
                "bool" => new TypeShape(KType.Bool) { Declared = depth == 0 },
                "list" => new TypeShape(KType.List),
                "map" => new TypeShape(KType.Map),
                "fn" => new TypeShape(KType.Fn),
                "date" => new TypeShape(KType.Date),
                "email" => new TypeShape(KType.Email) { Declared = depth == 0 },
                "password" => new TypeShape(KType.Password) { Declared = depth == 0 },
                "handle" => new TypeShape(KType.Handle) { Declared = depth == 0 },
                "uuid" => new TypeShape(KType.Uuid) { Declared = depth == 0 },
                "regex" => new TypeShape(KType.Regex) { Declared = depth == 0 },
                "any" => TypeShape.Unknown,
                _ => null,
            };
            if (shape == null && NativeTypes.Get(t) is NativeTypeInfo native)
            {
                // port is a number; the other native types are text with a meaning of their own.
                shape = new TypeShape(native.Name == "port" ? KType.Number : native.Name) { Declared = depth == 0 };
            }
            if (shape == null && t.Length > 0)
            {
                Symbol symbol = result.RootScope.Innermost(offset).Lookup(t, offset);
                shape = symbol?.EnumValues != null ? new TypeShape(KType.String)
                    : symbol?.Shape.Type == KType.Type ? symbol.Shape.Instance
                    // An <enum> declared further down, or before its scope is walked, is still a string.
                    : result.Markup.DescendantElements().Any(e => e.Name == "enum" && e.GetAttributeValue("name")?.Trim() == t) ? new TypeShape(KType.String)
                    : null;
            }
            for (int i = 0; i < depth && shape != null; i++)
            {
                shape = new TypeShape(KType.List) { Element = shape };
            }
            return shape;
        }

        /// <summary>The type without the arguments given where it is used: <c>uuid(v: 4)</c> is uuid, <c>string[](max: 5)</c> is string[].</summary>
        internal static string WithoutTypeArguments(string text)
        {
            int open = text.IndexOf('(');
            if (open <= 0)
            {
                return text;
            }
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0)
                {
                    return (text.Substring(0, open) + text.Substring(i + 1)).Trim();
                }
            }
            return text.Substring(0, open).Trim();
        }

        /// <summary>The user function (declared or imported) a call names, or null for a built-in command.</summary>
        private Symbol FunctionNamed(string name)
        {
            // Functions are hoisted into the root scope.
            Symbol s = result.RootScope.Lookup(name, int.MaxValue);
            return s?.Kind == SymbolKind.Function ? s : null;
        }

        /// <summary>
        /// The type whose methods a value uses for <paramref name="name"/>: a model's or record's own (KScript.Data's
        /// <c>$User->where()</c>, <c>$user->save()</c>) when it has that method, otherwise its plain type's.
        /// </summary>
        internal string ReceiverType(string name, TypeShape receiver) =>
            receiver.Kind != null && catalog.GetMethods(name, receiver.Kind).Any(m => m.ReceiverType == receiver.Kind) ? receiver.Kind : receiver.Type;

        /// <summary>
        /// What a KScript.Data method gives, in terms of the model's records: a query on $User is a query of Users,
        /// ->get() a list of them, and ->first() one.
        /// </summary>
        private static TypeShape ModelResult(CallableInfo method, TypeShape receiver)
        {
            TypeShape record = receiver.Kind == KType.Model ? receiver.Instance
                : receiver.Type == KType.Query ? receiver.Instance
                : receiver.Kind == KType.Record ? receiver
                : null;
            if (record == null || method.Package != "KScript.Data")
            {
                return null;
            }
            switch (method.ReturnType)
            {
                case KType.Query:
                    return new TypeShape(KType.Query) { Name = record.Name, Instance = record };
                case KType.Record:
                    return record;
                case KType.List when method.Name is "get" or "all":
                    return new TypeShape(KType.List) { Element = record };
                case KType.Any when method.Name is "first" or "first_or_fail" or "find" or "find_or_fail":
                    return record;
                default:
                    return null;
            }
        }

        /// <summary>What a method returns when called on a value of this shape.</summary>
        public static TypeShape MethodResult(CallableInfo method, TypeShape receiver)
        {
            if (receiver.Type == KType.List)
            {
                if (SameShapeListMethods.Contains(method.Name))
                {
                    return receiver;
                }
                if (ItemListMethods.Contains(method.Name))
                {
                    return receiver.Element ?? TypeShape.Unknown;
                }
            }
            return new TypeShape(method.ReturnType);
        }

        private TypeShape InferMethod(string name, TypeShape receiver, int argCount)
        {
            if (receiver.Type == KType.List)
            {
                if (SameShapeListMethods.Contains(name))
                {
                    return receiver;
                }
                if (ItemListMethods.Contains(name))
                {
                    return receiver.Element ?? TypeShape.Unknown;
                }
            }

            string type = ReceiverType(name, receiver);
            List<CallableInfo> methods = catalog.GetMethods(name, type).Where(m => KType.AcceptsMethod(m.ReceiverType, m.Name, type)).ToList();
            CallableInfo method = methods.FirstOrDefault(m => argCount >= m.MinArgs && argCount <= m.MaxArgs) ?? methods.FirstOrDefault();
            if (method != null && ModelResult(method, receiver) is TypeShape records)
            {
                return records;
            }
            return method != null ? new TypeShape(method.ReturnType) : TypeShape.Unknown;
        }

    }
}
