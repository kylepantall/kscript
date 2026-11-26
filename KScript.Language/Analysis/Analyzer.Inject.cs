using System.Collections.Generic;
using System.Linq;

using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Text;
using KScript.Language.Catalog;
using KScript.Syntax.Markup;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        /// <summary>
        /// What @inject() is asked to find, checked before it runs: the model it is given, or as the default of an
        /// input the model the input's type names. The messages are <see cref="Injection"/>'s, so the editor and a
        /// running script say the same.
        /// </summary>
        private void CheckInject(IReadOnlyList<ArgumentNode> arguments, ExpressionRegion region, TextSpan callSpan)
        {
            ArgumentNode model = arguments.FirstOrDefault(a => a.Name == null || a.Name == "model");
            if (model != null)
            {
                TypeShape shape = Infer(model.Value, region);
                if (shape.Kind == KType.Model || shape.Type == KType.Any) return;
                Report(region.Source.ToSource(model.Value.Span), DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                    shape.Type == KType.Type && shape.Name != null ? Injection.NotModel(shape.Name) : Injection.TakesModel(shape.Display()));
                return;
            }
            MarkupElement field = region.Element;
            if (field == null || !IsKScriptElementName(field, "field") || region.Attribute?.Name != "default")
            {
                Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch, Injection.NeedsModel);
                return;
            }
            string type = field.GetAttributeValue("type")?.Trim() ?? KType.Any;
            string name = field.GetAttributeValue("name")?.Trim().TrimEnd('?');
            MarkupElement declaration = TypeElementNamed(type.TrimEnd('?'));
            if (declaration != null ? !IsModelElement(declaration) : IsBuiltInTypeName(type.TrimEnd('?')) || type.Contains('['))
            {
                Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.TypeMismatch,
                    declaration != null ? Injection.NotModel(type) : Injection.InputIsNotModel(name, type));
            }
        }
    }
}
