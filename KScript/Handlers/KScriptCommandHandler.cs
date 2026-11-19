using KScript.CommandHandler;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace KScript.Handlers
{
    /// <summary>
    /// Evaluates KScript expressions found within strings: commands (<c>@to_lower('X')</c>),
    /// method calls (<c>$name->upper()</c>), pipes (<c>$name |> trim()</c>) and lambdas (<c>fn($s) => $s->upper()</c>).
    /// </summary>
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptCommandHandler
    {
        /// <summary>Lambdas may call lambdas; stop runaway recursion with a clear error.</summary>
        private const int MaxLambdaDepth = 64;

        [ThreadStatic]
        private static int lambdaDepth;

        /// <summary>
        /// Returns true when the string contains at least one expression (command, method call, pipe or lambda).
        /// </summary>
        public static bool IsCommand(string str) => CommandExpressionParser.ContainsCommand(str);

        /// <summary>
        /// Evaluates a raw script string: handles '\n' escapes and surrounding quotes, parses it, then substitutes
        /// variables in the text and evaluates every expression.
        ///
        /// Variables are resolved after parsing, so a value containing ',', ')' or quotes can never change how
        /// a command's arguments are split, and values are never re-interpreted as commands.
        /// </summary>
        public static string Interpolate(string raw, KScriptContainer container, KScriptBaseObject parent)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return raw;
            }

            string text = Regex.Replace(raw, @"\\n", Environment.NewLine);
            text = KScriptStringHandler.StripSurroundingQuotes(text);

            if (!IsCommand(text))
            {
                return KScriptVariableHandler.ReturnFormattedVariables(container, text);
            }

            StringBuilder builder = new StringBuilder();
            foreach (Expr expr in CommandExpressionParser.Parse(text))
            {
                builder.Append(expr is TextExpr t
                    ? KScriptVariableHandler.ReturnFormattedVariables(container, t.Text)
                    : EvaluateExpression(expr, container, parent));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Evaluates every expression in <paramref name="str"/>; text between expressions is left untouched.
        /// </summary>
        /// <param name="parse_immediately">Kept for backwards compatibility; expressions are always evaluated in order.</param>
        public static string HandleCommands(string str, KScriptContainer container, KScriptBaseObject parent, bool parse_immediately = false)
        {
            if (!IsCommand(str))
            {
                return str;
            }

            StringBuilder builder = new StringBuilder();
            foreach (Expr expr in CommandExpressionParser.Parse(str))
            {
                builder.Append(expr is TextExpr t ? t.Text : EvaluateExpression(expr, container, parent));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Splits a comma separated argument list (honouring quotes, brackets and expressions)
        /// and evaluates each argument.
        /// </summary>
        public static string[] EvaluateArgumentList(string arguments, KScriptContainer container, KScriptBaseObject parent)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return new string[0];
            }

            return EvaluateArguments(CommandExpressionParser.ParseArgumentList(arguments), container, parent);
        }

        #region Expression evaluation

        public static string EvaluateExpression(Expr expr, KScriptContainer container, KScriptBaseObject parent)
        {
            switch (expr)
            {
                case TextExpr text:
                    return text.Text;

                case LiteralExpr literal:
                    // Quoted strings substitute variables, as they always have: '$name' gives the value of name.
                    return FormatVariables(literal.Value, container);

                case VariableExpr variable:
                    return GetVariable(variable.Name, container);

                case CommandExpr command:
                    return EvaluateCommand(command, EvaluateArguments(command.Arguments, container, parent), container, parent);

                case MethodExpr method:
                    return InvokeMethod(method.Name, EvaluateExpression(method.Target, container, parent),
                        EvaluateArguments(method.Arguments, container, parent),
                        (method.Target as VariableExpr)?.Name, container, parent);

                case PipeExpr pipe:
                    return EvaluatePipe(pipe, container, parent);

                case LambdaExpr lambda:
                    // A lambda on its own evaluates to its source, so it can be stored in a def and called later.
                    return lambda.Source;

                case MethodRefExpr methodRef:
                    throw new KScriptException("Syntax_Error", $"'{methodRef.Source}' must follow a value, e.g. $value->{methodRef.Name}(...) or $value |> {methodRef.Source}.");

                default:
                    throw new KScriptException($"Cannot evaluate '{expr.Source}'.");
            }
        }

        private static string EvaluatePipe(PipeExpr pipe, KScriptContainer container, KScriptBaseObject parent)
        {
            string value = EvaluateExpression(pipe.Left, container, parent);
            string variableName = (pipe.Left as VariableExpr)?.Name;

            switch (pipe.Right)
            {
                case MethodRefExpr method:
                    return InvokeMethod(method.Name, value, EvaluateArguments(method.Arguments, container, parent), variableName, container, parent);

                case CommandExpr command:
                    // The piped value becomes the command's first argument: $x |> @split(',', '1') is @split($x, ',', '1').
                    string[] args = new[] { value }.Concat(EvaluateArguments(command.Arguments, container, parent)).ToArray();
                    return EvaluateCommand(command, args, container, parent);

                case LambdaExpr lambda:
                    return InvokeLambda(lambda, new[] { value }, container, parent);

                default:
                    // A variable (or bracketed expression) holding a lambda: $x |> $shout
                    string stored = EvaluateExpression(pipe.Right, container, parent);
                    LambdaExpr storedLambda = CommandExpressionParser.ParseLambda(stored)
                        ?? throw new KScriptException("Type_Error", $"'{pipe.Right.Source}' is not a lambda (its value is '{stored}'), so it cannot be used after '|>'.");
                    return InvokeLambda(storedLambda, new[] { value }, container, parent);
            }
        }

        /// <summary>
        /// Calls a lambda. Parameters are bound as variables for the duration of the call and then restored.
        /// Missing arguments are empty strings; extra arguments are ignored.
        /// </summary>
        public static string InvokeLambda(LambdaExpr lambda, string[] args, KScriptContainer container, KScriptBaseObject parent)
        {
            if (++lambdaDepth > MaxLambdaDepth)
            {
                lambdaDepth = 0;
                throw new KScriptException("Recursion_Error", $"Lambdas nested more than {MaxLambdaDepth} deep - is a lambda calling itself? ({lambda.Source})");
            }

            List<(string name, bool existed, Arguments.def previous)> saved = new List<(string, bool, Arguments.def)>();

            try
            {
                for (int i = 0; i < lambda.Parameters.Count; i++)
                {
                    string name = lambda.Parameters[i];
                    bool existed = container.HasDef(name);
                    saved.Add((name, existed, existed ? container.GetDefs()[name] : null));
                    container[name] = new Arguments.def(i < args.Length ? args[i] : string.Empty);
                }

                return EvaluateExpression(lambda.Body, container, parent);
            }
            finally
            {
                lambdaDepth--;

                for (int i = saved.Count - 1; i >= 0; i--)
                {
                    if (saved[i].existed)
                    {
                        container[saved[i].name] = saved[i].previous;
                    }
                    else
                    {
                        container.RemoveDef(saved[i].name);
                    }
                }
            }
        }

        private static string InvokeMethod(string name, string value, string[] args, string variableName, KScriptContainer container, KScriptBaseObject parent)
        {
            if (string.Equals(name, "call", StringComparison.OrdinalIgnoreCase))
            {
                LambdaExpr lambda = CommandExpressionParser.ParseLambda(value)
                    ?? throw new KScriptException("Type_Error", $"call() needs a lambda, but the value is '{value}'.");
                return InvokeLambda(lambda, args, container, parent);
            }

            MethodCallContext context = new MethodCallContext(name, value, args, variableName, container);

            if (ValueMethods.TryInvoke(context, out string result))
            {
                return result;
            }

            // Older variable functions in KScript.VariableFunctions (e.g. to_array, paragraph) work on a def.
            if (variableName != null && container.LoadedVariableFunctions.TryGetValue(name.ToLowerInvariant(), out Type legacy)
                && typeof(IVariableFunction).IsAssignableFrom(legacy))
            {
                IVariableFunction func = KScriptParser.GetVariableFunction(legacy, container, variableName);
                return func.Evaluate(args);
            }

            throw new KScriptException("Unknown_Method",
                $"Unknown method '{name}()'. Available methods: {string.Join(", ", ValueMethods.Describe().Select(m => m.Key))}.");
        }

        private static string GetVariable(string name, KScriptContainer container)
        {
            if (container.HasDef(name) || container.Properties.DynamicDefs)
            {
                return container.GetDef(name).Contents;
            }

            throw new KScriptDefNotFound(null, $"The def with the id '{name}' could not be found.");
        }

        private static string EvaluateCommand(CommandExpr command, string[] args, KScriptContainer container, KScriptBaseObject parent)
        {
            string[] @params = args.Select(a => MultiArray.MultiArrayParser.HandleString(a, container)).ToArray();

            Type type = GetCommandType(command.Name);

            if (type == null)
            {
                throw new KScriptException("Unknown_Command", $"Unknown command '@{command.Name}' in '{command.Source}'.");
            }

            KScriptCommand cmd = GetCommandObject(@params, type, container, parent);

            try
            {
                return cmd.Calculate() ?? string.Empty;
            }
            catch (KScriptException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new KScriptException(cmd, $"Command '{command.Source}' failed: {ex.Message}", ex);
            }
        }

        private static string[] EvaluateArguments(List<ArgumentExpr> arguments, KScriptContainer container, KScriptBaseObject parent) =>
            arguments.Select(arg => EvaluateArgument(arg, container, parent)).ToArray();

        private static string EvaluateArgument(ArgumentExpr argument, KScriptContainer container, KScriptBaseObject parent)
        {
            List<Expr> parts = argument.Parts;
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < parts.Count; i++)
            {
                Expr part = parts[i];

                if (part is TextExpr raw)
                {
                    string text = raw.Text;

                    // Whitespace around an argument (e.g. after the comma in "@x('a', 'b')") is not part of its value.
                    if (i == 0) text = text.TrimStart();
                    if (i == parts.Count - 1) text = text.TrimEnd();

                    builder.Append(FormatVariables(text, container));
                }
                else if (part is LiteralExpr literal)
                {
                    builder.Append(FormatVariables(literal.Value, container));
                }
                else
                {
                    builder.Append(EvaluateExpression(part, container, parent));
                }
            }

            return builder.ToString();
        }

        private static string FormatVariables(string text, KScriptContainer container)
        {
            if (container == null || string.IsNullOrEmpty(text) || (text.IndexOf('$') < 0 && text.IndexOf('~') < 0))
            {
                return text;
            }

            return KScriptVariableHandler.ReturnFormattedVariables(container, text);
        }

        #endregion


        /// <summary>
        /// Creates a command object, choosing the constructor that matches the number of arguments.
        /// Supports optional parameters and <c>params string[]</c>.
        /// </summary>
        public static KScriptCommand GetCommandObject(string[] @params, Type _type, KScriptContainer container, KScriptBaseObject parent)
        {
            if (_type == null)
            {
                throw new ArgumentNullException(nameof(_type));
            }

            @params = @params ?? Array.Empty<string>();
            object[] invokeArgs = null;
            ConstructorInfo match = null;

            foreach (ConstructorInfo ctor in _type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length))
            {
                if (TryBind(ctor, @params, out invokeArgs))
                {
                    match = ctor;
                    break;
                }
            }

            if (match == null)
            {
                throw new KScriptException("Invalid_Command_Arguments",
                    $"Command '@{_type.Name}' does not accept {@params.Length} argument(s). Accepted forms: " +
                    string.Join(", ", _type.GetConstructors().Select(DescribeConstructor)));
            }

            KScriptCommand obj;
            try
            {
                obj = (KScriptCommand)match.Invoke(invokeArgs);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                if (ex.InnerException is KScriptException)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }

                throw new KScriptException("Invalid_Command_Arguments",
                    $"Invalid arguments for '@{_type.Name}({string.Join(", ", @params.Select(p => "'" + p + "'"))})': {ex.InnerException.Message}");
            }

            obj.Init(container, parent);
            return obj;
        }

        public static KScriptCommand GetCommandObject(Type _type, KScriptContainer container, KScriptBaseObject parent)
            => _type == null ? null : GetCommandObject(Array.Empty<string>(), _type, container, parent);

        private static bool TryBind(ConstructorInfo ctor, string[] args, out object[] invokeArgs)
        {
            invokeArgs = null;
            ParameterInfo[] parameters = ctor.GetParameters();

            if (parameters.Any(p => p.ParameterType != typeof(string) && p.ParameterType != typeof(string[])))
            {
                return false;
            }

            bool hasParamsArray = parameters.Length > 0
                && parameters[parameters.Length - 1].ParameterType == typeof(string[])
                && parameters[parameters.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);

            int fixedCount = hasParamsArray ? parameters.Length - 1 : parameters.Length;
            int required = parameters.Take(fixedCount).Count(p => !p.HasDefaultValue);

            if (args.Length < required || (!hasParamsArray && args.Length > fixedCount))
            {
                return false;
            }

            invokeArgs = new object[parameters.Length];

            for (int i = 0; i < fixedCount; i++)
            {
                invokeArgs[i] = i < args.Length ? args[i] : parameters[i].DefaultValue;
            }

            if (hasParamsArray)
            {
                invokeArgs[parameters.Length - 1] = args.Skip(fixedCount).ToArray();
            }

            return true;
        }

        private static string DescribeConstructor(ConstructorInfo ctor)
        {
            IEnumerable<string> names = ctor.GetParameters().Select(p =>
                p.IsDefined(typeof(ParamArrayAttribute), false) ? p.Name + "..." : (p.HasDefaultValue ? p.Name + "?" : p.Name));
            return $"@{ctor.DeclaringType.Name}({string.Join(", ", names)})";
        }

        /// <summary>
        /// Looks up a command type by name (case-insensitive). Returns null when no such command exists.
        /// </summary>
        public static Type GetCommandType(string type_name)
        {
            if (string.IsNullOrWhiteSpace(type_name))
            {
                return null;
            }

            Type type = Assembly.GetExecutingAssembly().GetType($"{Global.GlobalIdentifiers.COMMANDS_NAMESPACE}.{type_name.ToLowerInvariant()}");
            return type != null && typeof(KScriptCommand).IsAssignableFrom(type) && !type.IsAbstract ? type : null;
        }
    }
}
