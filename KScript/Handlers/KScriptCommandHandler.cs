using KScript.CommandHandler;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace KScript.Handlers
{
    /// <summary>
    /// Evaluates KScript commands (e.g. <c>@to_lower('X')</c>) found within strings.
    /// </summary>
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptCommandHandler
    {
        /// <summary>
        /// Returns true when the string contains at least one command call.
        /// </summary>
        public static bool IsCommand(string str) => CommandExpressionParser.ContainsCommand(str);

        /// <summary>
        /// Replaces every command call in <paramref name="str"/> with its result.
        /// Text outside of command calls is left untouched.
        /// </summary>
        /// <param name="parse_immediately">Kept for backwards compatibility; commands are always evaluated in order.</param>
        public static string HandleCommands(string str, KScriptContainer container, KScriptBaseObject parent, bool parse_immediately = false)
        {
            if (!IsCommand(str))
            {
                return str;
            }

            List<CommandSegment> segments = CommandExpressionParser.Parse(str);
            StringBuilder builder = new StringBuilder();

            foreach (CommandSegment segment in segments)
            {
                if (segment is CommandCall call)
                {
                    builder.Append(Evaluate(call, container, parent));
                }
                else
                {
                    builder.Append(((TextSegment)segment).Text);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Evaluates a single parsed command call, evaluating nested calls in its arguments first.
        /// </summary>
        public static string Evaluate(CommandCall call, KScriptContainer container, KScriptBaseObject parent)
        {
            string[] @params = call.Arguments
                .Select(arg => MultiArray.MultiArrayParser.HandleString(EvaluateArgument(arg, container, parent), container))
                .ToArray();

            Type type = GetCommandType(call.Name);

            if (type == null)
            {
                throw new KScriptException("Unknown_Command", $"Unknown command '@{call.Name}' in '{call.Source}'.");
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
                throw new KScriptException(cmd, $"Command '{call.Source}' failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Splits a comma separated argument list (honouring quotes, brackets and nested commands)
        /// and evaluates each argument.
        /// </summary>
        public static string[] EvaluateArgumentList(string arguments, KScriptContainer container, KScriptBaseObject parent)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return new string[0];
            }

            List<CommandSegment> segments = CommandExpressionParser.Parse("@__arguments(" + arguments + ")");

            if (segments.Count != 1 || !(segments[0] is CommandCall call))
            {
                throw new KScriptException($"Invalid argument list '{arguments}'.");
            }

            return call.Arguments.Select(arg => EvaluateArgument(arg, container, parent)).ToArray();
        }

        private static string EvaluateArgument(CommandArgument argument, KScriptContainer container, KScriptBaseObject parent)
        {
            List<ArgumentPart> parts = argument.Parts;
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < parts.Count; i++)
            {
                ArgumentPart part = parts[i];

                switch (part.Kind)
                {
                    case ArgumentPartKind.Command:
                        builder.Append(Evaluate(part.Command, container, parent));
                        break;

                    case ArgumentPartKind.Quoted:
                        builder.Append(FormatVariables(part.Text, container));
                        break;

                    default:
                        string text = part.Text;

                        // Whitespace around an argument (e.g. after the comma in "@x('a', 'b')") is not part of its value.
                        if (i == 0) text = text.TrimStart();
                        if (i == parts.Count - 1) text = text.TrimEnd();

                        builder.Append(FormatVariables(text, container));
                        break;
                }
            }

            return builder.ToString();
        }

        private static string FormatVariables(string text, KScriptContainer container)
        {
            if (container == null || string.IsNullOrEmpty(text) || text.IndexOf('$') < 0)
            {
                return text;
            }

            return KScriptVariableHandler.ReturnFormattedVariables(container, text);
        }

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
