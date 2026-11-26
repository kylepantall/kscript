using KScript.Syntax;
using KScript.Syntax.Text;
using KScript.Syntax.Markup;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        /// <summary>The component whose html block is being checked: its elements aren't children of the type.</summary>
        private MarkupElement componentHtmlOwner;

        /// <summary>The type a private field or method of <paramref name="receiver"/> belongs to, or null when any code can use it.</summary>
        internal static string PrivateOwner(TypeShape receiver, string name, bool method)
        {
            TypeShape shape = receiver?.Instance ?? receiver;
            if (shape == null) return null;
            if (!method) return shape.Fields.TryGetValue(name, out FieldInfo field) ? field.PrivateTo : null;
            return shape.Methods.TryGetValue(name, out Symbol fn) && TypeBody.IsPrivate(fn.DeclaringElement)
                ? fn.DeclaringElement.Parent?.GetAttributeValue("name")?.Trim() : null;
        }

        /// <summary>Reports a private field or method used by code outside the type that declares it.</summary>
        private void CheckPrivate(TypeShape receiver, string name, bool method, TextSpan span, ExpressionRegion region)
        {
            if (PrivateOwner(receiver, name, method) is not string owner) return;
            // Inside the type, or a type joined with it, which was given the same member.
            foreach (MarkupElement e in Enclosing(region.Element))
            {
                if (e.Name is "type" or "model" && result.DeclaredTypes.TryGetValue(e, out TypeShape inside) && PrivateOwner(inside, name, method) == owner) return;
            }
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownField, TypeBody.PrivateMessage(name, owner));
        }

        /// <summary>An element and what it is written in. A component's html block is written in the component.</summary>
        private IEnumerable<MarkupElement> Enclosing(MarkupElement start)
        {
            for (MarkupElement e = start; e != null; e = e.Parent) yield return e;
            for (MarkupElement e = componentHtmlOwner; e != null; e = e.Parent) yield return e;
        }
    }
}
