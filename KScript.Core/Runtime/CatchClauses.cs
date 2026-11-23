using System.Runtime.ExceptionServices;
using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    /// <summary>A type-wide catch keeps the scope and module of the declaration, including when inherited.</summary>
    internal sealed record TypeCatch(MarkupElement Clause, Scope Closure, Module Module, bool RecordFields)
    {
        internal static TypeCatch[] Collect(ElementRun run, IEnumerable<TypeCatch> inherited, bool recordFields) =>
            run.ChildElements.Where(e => e.Name == "catch").Select(e => new TypeCatch(e, run.Scope, run.Module, recordFields))
                .Concat(inherited).Distinct().ToArray();
    }

    internal static class CatchClauses
    {
        /// <summary>An unmatched clause returns so the next can be tried. A matching declaration catch must
        /// return a non-null value to its caller or propagate an error.</summary>
        internal static void HandleDeclaration(Interpreter interpreter, MarkupElement clause, Scope scope, Module module, KScriptException error)
        {
            try
            {
                if (!Handle(interpreter, clause, scope, module, error)) return;
            }
            catch (ReturnSignal r) when (Values.Unwrap(r.Value) == null)
            {
                // A bare return or a null expression has the same effect as reaching the end of the handler.
            }
            ExceptionDispatchInfo.Throw(error);
        }

        internal static Scope BindReceiver(Scope closure, object receiver, bool recordFields)
        {
            Scope scope = closure.Child();
            if (recordFields && receiver is Record record)
            {
                foreach (var field in record) scope.Declare(field.Key, field.Value);
            }
            if (!recordFields) scope.Declare("value", Values.Unwrap(receiver));
            scope.Declare("this", receiver);
            return scope;
        }

        internal static bool Handle(Interpreter interpreter, MarkupElement clause, Scope parent, Module module, KScriptException error)
        {
            if (clause.GetAttributeValue("type") is string types && !CorePackage.ErrorNames(types).Any(t => ErrorTypes.Matches(error, t))) return false;
            Scope scope = parent.Child();
            string name = clause.GetAttributeValue("as")?.Trim().TrimStart('$') ?? "err";
            scope.Declare(name, error.ToValue());
            if (clause.GetAttribute("when")?.Value is DecodedText guard && !Values.Truthy(interpreter.EvaluateText(guard, scope, module))) return false;
            using (ErrorTypes.Handle(error)) interpreter.ExecuteChildren(clause, scope, module);
            return true;
        }
    }
}
