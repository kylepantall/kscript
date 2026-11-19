using System.Reflection;

using KScript.Handlers;
using KScript.KScriptObjects;

using A = KScript.Language.Catalog.AttributeInfo;
using KScript.Metadata;

using K = KScript.Metadata.KScriptValueKind;

namespace KScript.Language.Catalog
{
    /// <summary>
    /// Everything the tooling knows about KScript 2: elements and their attributes, commands, methods, retired v1
    /// syntax and built-in variables.
    ///
    /// The interpreter is the source of truth. Element classes carry [KScriptElement], [KScriptDoc],
    /// [KScriptAttribute] and [KScriptProperty]; command classes carry [KScriptDoc] and [KScriptParam]; anything
    /// KScript 2 removed carries [KScriptRetired]; methods come from <see cref="ValueMethods.Catalog"/>. This class
    /// reads all of that by reflection. The only things described here directly are the parts of the KScript 2
    /// proposal that have no implementation yet (marked <c>Planned</c>); once a class implements one, the class wins.
    /// </summary>
    public sealed class LanguageCatalog
    {
        private static readonly Lazy<LanguageCatalog> DefaultInstance = new Lazy<LanguageCatalog>(() => new LanguageCatalog());

        private readonly Dictionary<string, ElementInfo> elements = new Dictionary<string, ElementInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RetiredInfo> retiredElements = new Dictionary<string, RetiredInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RetiredInfo> retiredAttributes = new Dictionary<string, RetiredInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CallableInfo> commands = new Dictionary<string, CallableInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RetiredInfo> retiredCommands = new Dictionary<string, RetiredInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<CallableInfo> methods = new List<CallableInfo>();
        private readonly Dictionary<string, (string Type, string Description)> builtInVariables = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, string>> typeFields = new Dictionary<string, Dictionary<string, string>>();

        public static LanguageCatalog Default => DefaultInstance.Value;

        public LanguageCatalog()
        {
            AddElements();
            LoadInterpreterElements();
            AddRetired();
            AddCommands();
            AddMethods();
            AddVariables();
        }

        #region Lookups

        public IEnumerable<ElementInfo> Elements => elements.Values;
        public IEnumerable<CallableInfo> Commands => commands.Values;
        public IEnumerable<CallableInfo> Methods => methods;
        public IEnumerable<string> RetiredElementNames => retiredElements.Keys;

        public ElementInfo GetElement(string name) => name != null && elements.TryGetValue(name, out ElementInfo e) ? e : null;

        public RetiredInfo GetRetiredElement(string name) => name != null && retiredElements.TryGetValue(name, out RetiredInfo r) ? r : null;

        public RetiredInfo GetRetiredAttribute(string name) => name != null && retiredAttributes.TryGetValue(name, out RetiredInfo r) ? r : null;

        public CallableInfo GetCommand(string name) => name != null && commands.TryGetValue(name, out CallableInfo c) ? c : null;

        public RetiredInfo GetRetiredCommand(string name) => name != null && retiredCommands.TryGetValue(name, out RetiredInfo r) ? r : null;

        /// <summary>Methods with this name, best match for the receiver type first.</summary>
        public IEnumerable<CallableInfo> GetMethods(string name, string receiverType = KType.Any) =>
            methods.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                   .OrderBy(m => m.ReceiverType == receiverType ? 0 : KType.Accepts(m.ReceiverType, receiverType) ? 1 : 2);

        public IEnumerable<CallableInfo> MethodsFor(string receiverType) =>
            methods.Where(m => KType.Accepts(m.ReceiverType, receiverType))
                   .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                   .Select(g => g.OrderBy(m => m.ReceiverType == receiverType ? 0 : 1).First());

        /// <summary>Variables that exist without being declared, such as <c>$request</c> in pages.</summary>
        public IReadOnlyDictionary<string, (string Type, string Description)> BuiltInVariables => builtInVariables;

        /// <summary>Known fields of a value type (<c>$request.query</c>, <c>$err.message</c>).</summary>
        public IReadOnlyDictionary<string, string> FieldsOf(string type) =>
            type != null && typeFields.TryGetValue(type, out Dictionary<string, string> f) ? f : new Dictionary<string, string>();

        #endregion

        #region Elements

        private void Add(ElementInfo e) => elements[e.Name] = e;

        private void AddElements()
        {
            // Variables
            Add(new ElementInfo("let", "Declares a variable in the current scope. The value goes in `value`, or in the element text for longer values. Without `name`, the text is assignment statements such as `x = 3`.",
                ContentKind.Expression,
                new A("name", K.DeclareVariable, "The variable to declare."),
                new A("value", K.Expression, "The initial value. Can be written as the element's text instead."),
                new A("type", K.Enum, "Convert the value to this type, or fail early.", false, KType.Declarable),
                new A("const", K.Bool, "yes makes a later <set> an error."))
            {
                Example = "<let name=\"count\" value=\"0\" />\n<let name=\"people\">[{ name: 'Joan', age: 82 }]</let>\n<let>x = 3</let>",
            });
            Add(new ElementInfo("set", "Changes an existing variable. Fails if the variable was never declared (a typo guard) or is const. Without `name`, the text is assignment statements.",
                ContentKind.Expression,
                new A("name", K.VariableReference, "The variable to change."),
                new A("value", K.Expression, "The new value. Can be written as the element's text instead."),
                new A("op", K.Enum, "Update instead of replace: op=\"+=\" value=\"1\".", false, "=", "+=", "-=", "*=", "/=", "??="))
            {
                Example = "<set name=\"total\" value=\"$total + $price * $qty\" />\n<set>count += 1</set>",
            });
            Add(new ElementInfo("do", "Runs assignment statements and calls, separated by `;`.", ContentKind.AssignmentStatements)
            {
                Example = "<do>$x = 3; $total += $price * $qty</do>",
            });

            Add(new ElementInfo("markdown", "Renders its Markdown text (with `{ }` values, escaped) to HTML.", ContentKind.Template));

            // Control flow
            AttributeInfo test = new A("test", K.Expression, "The condition, for example `$n lt 10 and $name ne ''`.", true);
            Add(new ElementInfo("elif", "Runs when the earlier <if>/<elif> tests were false and this test is true.", ContentKind.Statements, test)
            {
                MustFollow = new[] { "if", "elif" },
            });
            Add(new ElementInfo("while", "Loops while the test is true. `test=\"true\"` loops forever.", ContentKind.Statements, test) { IsLoop = true });
            Add(new ElementInfo("match", "Compares one value against several cases.", ContentKind.Statements,
                new A("value", K.Expression, "The value to compare.", true))
            {
                Example = "<match value=\"$choice\">\n  <case is=\"1\"><echo>One</echo></case>\n  <case is=\"2, 3\"><echo>Two or three</echo></case>\n  <case when=\"$choice gt 9\"><echo>Big</echo></case>\n  <default><echo>Unknown</echo></default>\n</match>",
            });
            Add(new ElementInfo("case", "A branch of <match>: runs when the value equals one of `is`, or when `when` is true.", ContentKind.Statements,
                new A("is", K.ExpressionList, "Values to compare with, comma separated."),
                new A("when", K.Expression, "A condition."))
            {
                MustBeInside = new[] { "match" },
            });
            Add(new ElementInfo("default", "The branch of <match> that runs when no case matched.", ContentKind.Statements) { MustBeInside = new[] { "match" } });

            // Functions
            Add(new ElementInfo("fn", "Declares a function, called like a command: `@name(args)`, or piped: `$x |> @name()`.", ContentKind.Statements,
                new A("name", K.DeclareFunction, "The function's name.", true),
                new A("params", K.Parameters, "Parameters, with optional types and defaults: `who, greeting = 'Hello'` or `count: int = 1`."),
                new A("value", K.Expression, "For one-line functions: the result, instead of a body."),
                new A("description", K.Text, "What the function does. Shown in hover, and to a model when used as an <llm> tool."))
            {
                Example = "<fn name=\"greet\" params=\"who, greeting = 'Hello'\">\n  <return value=\"$greeting + ', ' + $who + '!'\" />\n</fn>\n<echo>@greet('World')</echo>",
            });
            Add(new ElementInfo("return", "Ends the function with a value.", ContentKind.Empty,
                new A("value", K.Expression, "The result. Without it the function returns null."))
            {
                MustBeInside = new[] { "fn" },
            });
            Add(new ElementInfo("spawn", "Runs a function in the background.", ContentKind.Empty,
                new A("fn", K.FunctionReference, "The function to run.", true),
                new A("args", K.ExpressionList, "Arguments, comma separated."))
            {
            });
            Add(new ElementInfo("import", "Imports the top-level <fn> functions and <let const> values of another script.", ContentKind.Empty,
                new A("src", K.Path, "The script to import, relative to this file.", true),
                new A("as", K.DeclareVariable, "Import everything under a prefix: `str.slugify()`."),
                new A("names", K.FunctionReferences, "Import only these names, callable directly: `@slugify()`."))
            {
            });

            Add(new ElementInfo("finally", "Runs after the <try> body and any <catch>, whether or not there was an error.", ContentKind.Statements) { MustBeInside = new[] { "try" } });
            Add(new ElementInfo("item", "An entry of <marray>. With `key` it is a map entry, without it a list item.", ContentKind.Template,
                new A("key", K.Text, "The key in the enclosing map."))
            {
                MustBeInside = new[] { "marray", "item" },
            });

            // LLMs
            Add(new ElementInfo("llm", "Sends its text as a prompt to a language model and stores the answer.", ContentKind.Template,
                new A("to", K.DeclareVariable, "The variable that receives the answer."),
                new A("system", K.Template, "The system prompt."),
                new A("schema", K.Expression, "Ask for structured output: a map describing the fields, such as `{ category: 'bug|feature', urgency: 'int 1-5' }`."),
                new A("tools", K.FunctionReferences, "<fn> functions the model may call, comma separated."),
                new A("model", K.Text, "The model to use. Defaults to claude-opus-5-5."),
                new A("stream-into", K.Selector, "Stream the answer into this page element as it is generated."))
            {
                Example = "<llm to=\"reply\" system=\"Answer in Markdown.\">\n  Customer {$customer.name} wrote: {$message}\n</llm>",
            });

            // Pages and browser
            Add(new ElementInfo("render", "Replaces the content of a page element. Non-KScript elements inside are output as HTML, with `{ }` values escaped.", ContentKind.Markup,
                new A("into", K.Selector, "The element to fill, as a CSS selector.", true),
                new A("raw", K.Bool, "yes outputs values without HTML escaping."))
            {
            });
            Add(new ElementInfo("respond", "Ends the request with this response (server pages).", ContentKind.Template,
                new A("status", K.Expression, "The HTTP status code."),
                new A("json", K.Expression, "Send this value as JSON."),
                new A("type", K.Text, "The Content-Type."))
            {
                Example = "<respond status=\"404\">Not found</respond>\n<respond json=\"$people\" />",
            });
            Add(new ElementInfo("redirect", "Redirects the visitor (server pages).", ContentKind.Empty,
                new A("to", K.Template, "Where to send the visitor.", true),
                new A("status", K.Expression, "The status code, 302 by default."))
            {
            });
            Add(new ElementInfo("layout", "Wraps the page in a layout file. Extra attributes become variables in the layout (`$title`).", ContentKind.Markup,
                new A("src", K.Path, "The layout file, such as _layout.html.", true))
            {
                AllowsExtraAttributes = true,
                Context = ElementContext.Page,
            });
            Add(new ElementInfo("slot", "In a layout or component: where the page's (or caller's) content goes.", ContentKind.Empty,
                new A("name", K.Text, "A named slot."))
            {
                Context = ElementContext.Page,
            });
            Add(new ElementInfo("on", "Runs its body when a DOM event fires (browser). `$event` holds the event.", ContentKind.Statements,
                new A("event", K.Text, "The event name, such as click or input.", true),
                new A("target", K.Selector, "The element to listen on.", true))
            {
            });

        }

        /// <summary>
        /// Reads elements from the interpreter: every class with [KScriptElement] becomes an element (replacing a
        /// planned description of the same name), and every class or property with [KScriptRetired] becomes a
        /// retirement. Classes with neither are listed in <see cref="UnclassifiedElementTypes"/>.
        /// </summary>
        private void LoadInterpreterElements()
        {
            foreach (Type type in InterpreterElementTypes())
            {
                string name = ElementName(type);
                if (type.Name.Contains('<'))
                {
                    continue;
                }
                KScriptRetiredAttribute retired = type.GetCustomAttribute<KScriptRetiredAttribute>();
                KScriptElementAttribute shape = type.GetCustomAttribute<KScriptElementAttribute>();

                if (retired != null)
                {
                    retiredElements[name] = new RetiredInfo(name, retired.Replacement, retired.Rename);
                }

                foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    KScriptRetiredAttribute r = prop.GetCustomAttribute<KScriptRetiredAttribute>();
                    if (r != null)
                    {
                        retiredAttributes[prop.Name] = new RetiredInfo(prop.Name, r.Replacement, r.Rename);
                    }
                }

                if (shape == null)
                {
                    if (retired == null)
                    {
                        unclassified.Add(type);
                    }
                    continue;
                }

                List<AttributeInfo> attributes = new List<AttributeInfo>();
                foreach (KScriptAttributeAttribute declared in type.GetCustomAttributes<KScriptAttributeAttribute>())
                {
                    attributes.Add(new A(declared.Name, declared.Kind, declared.Description, declared.Required, SplitList(declared.Options)));
                }

                foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    KScriptProperty doc = prop.GetCustomAttribute<KScriptProperty>();
                    if (doc == null || prop.IsDefined(typeof(KScriptRetiredAttribute))
                        || attributes.Any(x => string.Equals(x.Name, prop.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string[] options = prop.GetCustomAttribute<KScriptAcceptedOptions>()?.GetValues();
                    if (options != null && !options.All(o => System.Text.RegularExpressions.Regex.IsMatch(o, "^[A-Za-z0-9_-]+$")))
                    {
                        // v1 sometimes lists example values (paths, URLs) as "accepted options"; those aren't an enum.
                        options = null;
                    }
                    bool isBool = options != null && options.All(o => KScriptAcceptedOptions.BooleanValues.Contains(o));
                    AttributeKind kind = prop.GetCustomAttribute<KScriptValueAttribute>()?.Kind
                        ?? (isBool ? K.Bool : options != null ? K.Enum : K.Template);
                    attributes.Add(new A(prop.Name, kind, doc.ToString(), doc.IsRequired(), isBool ? null : options));
                }

                KScriptDocAttribute info = type.GetCustomAttribute<KScriptDocAttribute>();
                elements[name] = new ElementInfo(name, info?.Description ?? UsageOf(type), shape.Content, attributes.ToArray())
                {
                    Context = shape.Context,
                    Example = info?.Example,
                    MustFollow = SplitList(shape.MustFollow),
                    MustBeInside = SplitList(shape.MustBeInside),
                    IsLoop = shape.IsLoop,
                    AllowsExtraAttributes = shape.AllowsExtraAttributes,
                    HtmlWins = shape.HtmlWins,
                    ImplementedBy = type,
                };
            }
        }

        private readonly List<Type> unclassified = new List<Type>();

        /// <summary>
        /// Interpreter element classes that have neither [KScriptElement] nor [KScriptRetired]. The contract tests
        /// require this to be empty, so a new element can't be added without telling the tools about it.
        /// </summary>
        public IReadOnlyList<Type> UnclassifiedElementTypes => unclassified;

        /// <summary>Command classes with neither [KScriptDoc] nor [KScriptRetired] (also required to be empty).</summary>
        public IReadOnlyList<Type> UnclassifiedCommandTypes => unclassifiedCommands;

        private readonly List<Type> unclassifiedCommands = new List<Type>();

        /// <summary>All element classes in the interpreter.</summary>
        public static IEnumerable<Type> InterpreterElementTypes() =>
            typeof(KScriptObject).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.Namespace != null && t.Namespace.StartsWith("KScript.Arguments", StringComparison.Ordinal)
                            && typeof(KScriptObject).IsAssignableFrom(t));

        /// <summary>All command classes in the interpreter.</summary>
        public static IEnumerable<Type> InterpreterCommandTypes() =>
            typeof(KScriptCommand).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "KScript.Commands" && typeof(KScriptCommand).IsAssignableFrom(t));

        private static string ElementName(Type type) => type.Name.TrimStart('@');

        private static string UsageOf(Type type)
        {
            try
            {
                MethodInfo usage = type.GetMethod("UsageInformation", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
                if (usage != null && type.GetConstructor(Type.EmptyTypes) != null)
                {
                    return usage.Invoke(Activator.CreateInstance(type), null) as string ?? string.Empty;
                }
            }
            catch (Exception)
            {
                // Some elements need a script to construct; fall back to no description.
            }
            return string.Empty;
        }

        private static string[] SplitList(string list) =>
            string.IsNullOrWhiteSpace(list) ? Array.Empty<string>() : list.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        #endregion

        #region Retired v1 syntax

        private void AddRetired()
        {
            // Everything else that was retired is marked with [KScriptRetired] in the interpreter.
            retiredAttributes["version"] = new RetiredInfo("version", "There is only one KScript now; the version attribute isn't needed.");
            retiredAttributes["strict"] = new RetiredInfo("strict", "Strict checking is always on in KScript 2.");
        }

        #endregion

        #region Commands

        private void AddCommand(CallableInfo c) => commands[c.Name] = c;

        private static CallableInfo Planned(string name, string parameters, string description, string returns = KType.Any) =>
            new CallableInfo(CallableKind.Command, name, description, new[] { Signature.Parse(parameters) }) { ReturnType = returns, Planned = true };

        private void AddCommands()
        {
            foreach (Type type in InterpreterCommandTypes())
            {
                string name = type.Name.TrimStart('@');
                KScriptRetiredAttribute retired = type.GetCustomAttribute<KScriptRetiredAttribute>();
                if (retired != null)
                {
                    retiredCommands[name] = new RetiredInfo(name, retired.Replacement, retired.Rename);
                    continue;
                }

                KScriptDocAttribute doc = type.GetCustomAttribute<KScriptDocAttribute>();
                if (doc == null)
                {
                    unclassifiedCommands.Add(type);
                }

                Dictionary<string, KScriptParamAttribute> paramDocs = type.GetCustomAttributes<KScriptParamAttribute>()
                    .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

                List<Signature> signatures = type.GetConstructors()
                    .Select(c => new Signature(c.GetParameters().Select(p => new ParameterInfo(p.Name,
                        optional: p.HasDefaultValue,
                        variadic: p.IsDefined(typeof(ParamArrayAttribute), false),
                        description: paramDocs.TryGetValue(p.Name, out KScriptParamAttribute pd) ? pd.Description : null)).ToArray()))
                    .OrderBy(s => s.Parameters.Count)
                    .ToList();

                AddCommand(new CallableInfo(CallableKind.Command, name, doc?.Description ?? "Built-in command.", signatures)
                {
                    ReturnType = doc?.Returns ?? KType.String,
                    ImplementedBy = type,
                });
            }

            // From the KScript 2 proposal; not in the interpreter yet. A command class with the same name replaces these.
            foreach (CallableInfo planned in new[]
            {
                Planned("files", "folder, pattern?", "The files in a folder, as a list of file values (->name(), ->extension()...).", KType.List),
                Planned("read_json", "path", "Reads a JSON file into a value.", KType.Any),
                Planned("json", "text", "Parses JSON text into a value.", KType.Any),
                Planned("now", "", "The current date and time.", KType.Date),
                Planned("markdown", "text", "Renders Markdown: $result.html, $result.title, $result.meta.", KType.Map),
                Planned("llm", "prompt, model?", "Asks a language model and returns its answer.", KType.String),
                Planned("chat", "system?", "A conversation with a language model that keeps history: $chat->ask(text).", KType.Chat),
                Planned("dom", "selector", "A page element (browser): ->value(), ->text(), ->add_class()...", KType.Element),
                Planned("fetch", "url, options?", "An HTTP request: ->json(), ->text(), ->status().", KType.Response),
            })
            {
                if (!commands.ContainsKey(planned.Name))
                {
                    AddCommand(planned);
                }
            }
        }

        #endregion

        #region Methods

        private void AddMethod(string receiver, string name, string parameters, string description, string returns = KType.Any, bool planned = true) =>
            methods.Add(new CallableInfo(CallableKind.Method, name, description, new[] { Signature.Parse(parameters) })
            {
                ReceiverType = receiver,
                ReturnType = returns,
                Planned = planned,
            });

        private void AddMethods()
        {
            foreach ((string name, int min, int max, string description, string category) in ValueMethods.Catalog())
            {
                methods.Add(new CallableInfo(CallableKind.Method, name, description, new[] { SignatureFromDescription(name, min, max, description) })
                {
                    ReceiverType = category,
                    ReturnType = GuessReturnType(name, category, description),
                    ImplementedBy = typeof(ValueMethods),
                });
            }

            // Lists (KScript 2 proposal, section 5).
            AddMethod(KType.List, "count", "", "The number of items.", KType.Number);
            AddMethod(KType.List, "first", "", "The first item, or null.");
            AddMethod(KType.List, "last", "", "The last item, or null.");
            AddMethod(KType.List, "push", "item", "Adds an item to the end.", KType.List);
            AddMethod(KType.List, "pop", "", "Removes and returns the last item.");
            AddMethod(KType.List, "map", "fn", "A new list with fn applied to each item.", KType.List);
            AddMethod(KType.List, "filter", "fn", "The items for which fn is true.", KType.List);
            AddMethod(KType.List, "reduce", "fn, initial?", "Combines the items: fn($total, $item).");
            AddMethod(KType.List, "find", "fn", "The first item for which fn is true, or null.");
            AddMethod(KType.List, "any", "fn?", "yes if fn is true for any item (or the list isn't empty).", KType.Bool);
            AddMethod(KType.List, "all", "fn", "yes if fn is true for every item.", KType.Bool);
            AddMethod(KType.List, "sort", "fn?", "A sorted copy; fn($a, $b) compares.", KType.List);
            AddMethod(KType.List, "sort_by", "fn", "A copy sorted by fn($item).", KType.List);
            AddMethod(KType.List, "max_by", "fn", "The item with the largest fn($item).");
            AddMethod(KType.List, "min_by", "fn", "The item with the smallest fn($item).");
            AddMethod(KType.List, "join", "separator?", "The items joined into text.", KType.String);
            AddMethod(KType.List, "reverse", "", "A reversed copy.", KType.List);
            AddMethod(KType.List, "slice", "start, end?", "The items from start up to (not including) end.", KType.List);
            AddMethod(KType.List, "unique", "", "The list without duplicates.", KType.List);
            AddMethod(KType.List, "contains", "item", "yes if the list contains the item.", KType.Bool);
            AddMethod(KType.List, "is_empty", "", "yes if the list has no items.", KType.Bool);

            // Maps.
            AddMethod(KType.Map, "keys", "", "The keys, as a list.", KType.List);
            AddMethod(KType.Map, "values", "", "The values, as a list.", KType.List);
            AddMethod(KType.Map, "has", "key", "yes if the key exists.", KType.Bool);
            AddMethod(KType.Map, "get", "key, default?", "The value for a key, or the default.");
            AddMethod(KType.Map, "set", "key, value", "Sets a key.", KType.Map);
            AddMethod(KType.Map, "remove", "key", "Removes a key.", KType.Map);
            AddMethod(KType.Map, "entries", "", "The entries, as a list of { key, value } maps.", KType.List);
            AddMethod(KType.Map, "count", "", "The number of keys.", KType.Number);

            // Any value.
            AddMethod(KType.Any, "to_json", "", "The value as JSON text.", KType.String);

            // Values returned by commands.
            AddMethod(KType.String, "split", "separator", "In KScript 2, split(separator) returns a list of parts.", KType.List);
            AddMethod(KType.File, "name", "", "The file name with its extension.", KType.String);
            AddMethod(KType.File, "extension", "", "The extension, such as .txt.", KType.String);
            AddMethod(KType.File, "directory", "", "The folder the file is in.", KType.String);
            AddMethod(KType.File, "size", "", "The size in bytes.", KType.Number);
            AddMethod(KType.Date, "format", "pattern", "Formats the date, for example 'd MMMM yyyy'.", KType.String);
            AddMethod(KType.Response, "json", "", "The body parsed as JSON.");
            AddMethod(KType.Response, "text", "", "The body as text.", KType.String);
            AddMethod(KType.Response, "status", "", "The HTTP status code.", KType.Number);
            AddMethod(KType.Chat, "ask", "message", "Sends a message and returns the answer, keeping the history.", KType.String);
            AddMethod(KType.Element, "value", "", "The value of an input.", KType.String);
            AddMethod(KType.Element, "text", "", "The text content.", KType.String);
            AddMethod(KType.Element, "attr", "name, value?", "Reads or sets an attribute.", KType.String);
            AddMethod(KType.Element, "add_class", "name", "Adds a CSS class.", KType.Element);
            AddMethod(KType.Element, "remove_class", "name", "Removes a CSS class.", KType.Element);
            AddMethod(KType.Element, "show", "", "Shows the element.", KType.Element);
            AddMethod(KType.Element, "hide", "", "Hides the element.", KType.Element);
        }

        private static Signature SignatureFromDescription(string name, int min, int max, string description)
        {
            // Descriptions in ValueMethods start with the call shape when there are arguments: "replace(old, new)".
            string prefix = name + "(";
            if (description != null && description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                int close = description.IndexOf(')');
                if (close > prefix.Length - 1)
                {
                    Signature parsed = Signature.Parse(description.Substring(prefix.Length, close - prefix.Length));
                    if (parsed.MinArgs == min || max == int.MaxValue)
                    {
                        return parsed;
                    }
                }
            }

            List<ParameterInfo> ps = new List<ParameterInfo>();
            for (int i = 0; i < Math.Min(max, 4); i++)
            {
                ps.Add(new ParameterInfo("arg" + (i + 1), optional: i >= min));
            }
            if (max == int.MaxValue)
            {
                ps.Add(new ParameterInfo("args", variadic: true));
            }
            return new Signature(ps.ToArray());
        }

        private static string GuessReturnType(string name, string category, string description)
        {
            if (name.StartsWith("is_", StringComparison.Ordinal) || name.StartsWith("has_", StringComparison.Ordinal)
                || (description ?? string.Empty).StartsWith("yes if", StringComparison.Ordinal)
                || new[] { "equals", "contains", "starts_with", "ends_with", "lt", "lte", "mt", "mte", "between", "not", "and", "or", "xor", "positive", "negative", "not_zero" }.Contains(name))
            {
                return KType.Bool;
            }
            if (new[] { "length", "index_of", "count" }.Contains(name) || category == KType.Number && name != "default")
            {
                return KType.Number;
            }
            if (category == KType.String || name == "type")
            {
                return KType.String;
            }
            return KType.Any;
        }

        #endregion

        #region Variables

        private void AddVariables()
        {
            builtInVariables["request"] = (KType.Request, "The current HTTP request (server pages): method, path, query, form, headers, cookies, params, body.");
            builtInVariables["event"] = (KType.Map, "The DOM event inside <on>: value, key, target.");
            builtInVariables["args"] = (KType.List, "The script's command line arguments.");

            typeFields[KType.Request] = new Dictionary<string, string>
            {
                ["method"] = "GET, POST...",
                ["path"] = "The path, such as /people.",
                ["query"] = "Query string values: $request.query.page.",
                ["form"] = "Posted form fields: $request.form.email.",
                ["headers"] = "Request headers.",
                ["cookies"] = "Cookies.",
                ["params"] = "Route parameters from [name].html files: $request.params.id.",
                ["body"] = "The raw body: $request.body->json().",
            };
            typeFields[KType.Error] = new Dictionary<string, string>
            {
                ["type"] = "The error type, such as FileNotFound.",
                ["message"] = "What went wrong.",
                ["line"] = "The line the error came from.",
            };
        }

        #endregion
    }
}
