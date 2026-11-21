using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Jobs (KScript.Queue): <c>job SendWelcome(tries: 5) { user_id: int; fn handle() { } }</c> declarations, and the
    /// calls that make jobs (<c>SendWelcome(user_id: 7)</c>), whose named arguments must be the job's fields.
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <summary>The jobs this document declares, by name.</summary>
        private readonly Dictionary<string, DeclaredJob> declaredJobs = new Dictionary<string, DeclaredJob>(StringComparer.Ordinal);

        private sealed class DeclaredJob
        {
            public MarkupElement Element;
            public Symbol Symbol;
            public HashSet<string> Required = new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>The fields a job value has besides its own: .job (its type's name) and .fields.</summary>
        private static readonly string[] BaseJobMembers = { "job", "fields" };

        private bool IsJobElement(MarkupElement e) => IsKScriptElementName(e, "job") && catalog.GetElement("job")?.Package == "KScript.Queue";

        /// <summary>Declares every job before the walk, as the runtime hoists them, so a job can be dispatched above its declaration.</summary>
        private void HoistJobs(Scope root)
        {
            foreach (MarkupElement e in result.Markup.DescendantElements().Where(IsJobElement))
            {
                MarkupAttribute nameAttr = e.GetAttribute("name");
                if (nameAttr?.Value == null || !CheckIdentifier(nameAttr.Value))
                {
                    continue;
                }
                string name = nameAttr.ValueText.Trim();
                TextSpan span = TrimmedSpan(nameAttr.Value);
                if (!char.IsUpper(name[0]))
                {
                    string fixedName = char.ToUpperInvariant(name[0]) + name.Substring(1);
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue, $"Job names start with a capital letter, like types: {fixedName}.",
                        new[] { new QuickFix($"Change to {fixedName}", new TextChange(span, fixedName)) });
                    continue;
                }
                if (declaredJobs.ContainsKey(name))
                {
                    Report(span, DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"A job named '{name}' is already declared.");
                    continue;
                }
                TypeShape instance = new TypeShape(KType.Job) { Name = name };
                FillFields(e, instance);
                instance.Fields["job"] = new FieldInfo("job", new TypeShape(KType.String), null, "The job type's name.");
                instance.Fields["fields"] = new FieldInfo("fields", new TypeShape(KType.Map), null, "The job's fields, as a map.");
                var declared = new DeclaredJob
                {
                    Element = e,
                    Symbol = NewVariable(name, span, e, SymbolKind.Constant, new TypeShape(KType.JobType) { Name = name, Instance = instance }),
                };
                string description = e.GetAttributeValue("description");
                declared.Symbol.Description = (description != null ? description + "\n\n" : string.Empty) + "job " + name + " {\n"
                    + string.Concat(instance.Fields.Values.Where(f => !BaseJobMembers.Contains(f.Name)).Select(f => "  " + f.Name + ": " + f.Shape.Display() + "\n")) + "}";
                root.Declare(declared.Symbol, 0);
                declaredJobs[name] = declared;
                NoteRequiredJobFields(declared);
            }
        }

        private static void NoteRequiredJobFields(DeclaredJob declared)
        {
            var asError = new DeclaredError { Element = declared.Element };
            NoteRequiredFields(asError);
            declared.Required.UnionWith(asError.Required);
        }

        /// <summary>Walks a job: its options, its fields, and handle/failed with the fields, $this and $job as variables.</summary>
        private void AnalyzeJob(MarkupElement e, ElementInfo info, Scope scope, Scope inner, bool markupMode)
        {
            AnalyzeAttributes(e, info, scope);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            MarkupAttribute fields = e.GetAttribute("fields");
            if (fields?.Value != null)
            {
                foreach ((string part, int start) in SplitTopLevel(fields.Value.Value))
                {
                    CheckInlineField(part, start, fields.Value, seen);
                }
            }
            bool hasHandle = false;
            bool hasFailed = false;
            foreach (MarkupElement child in e.ChildElements)
            {
                if (IsKScriptElementName(child, "fn"))
                {
                    string fnName = child.GetAttributeValue("name")?.Trim();
                    if (fnName == "handle")
                    {
                        hasHandle = true;
                    }
                    else if (fnName == "failed")
                    {
                        hasFailed = true;
                    }
                    else if (child.GetAttribute("name")?.Value is DecodedText fnValue)
                    {
                        Report(TrimmedSpan(fnValue), DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, $"A job has two functions, handle and failed, not {fnName}. Put helpers outside the job.");
                    }
                    continue;
                }
                if (!IsKScriptElementName(child, "field"))
                {
                    Report(child.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MisplacedElement, $"<job> can only hold <field> elements and its handle and failed functions, not <{child.Name}>.");
                    continue;
                }
                MarkupAttribute nameAttr = child.GetAttribute("name");
                string name = nameAttr?.ValueText?.Trim().TrimEnd('?').TrimEnd();
                if (!string.IsNullOrEmpty(name) && !seen.Add(name))
                {
                    Report(TrimmedSpan(nameAttr.Value), DiagnosticSeverity.Error, DiagnosticCodes.DuplicateDeclaration, $"The field '{name}' is listed twice.");
                }
                string type = child.GetAttributeValue("type")?.Trim().TrimEnd('?');
                if (type == KType.Password && nameAttr?.Value != null)
                {
                    Report(TrimmedSpan(nameAttr.Value), DiagnosticSeverity.Error, DiagnosticCodes.InvalidAttributeValue,
                        $"The field '{name}' is a password, and queued jobs are saved to disk. Pass something that finds it instead, such as the user's id.");
                }
            }
            if (!hasHandle)
            {
                Report(e.NameSpan, DiagnosticSeverity.Error, DiagnosticCodes.MissingAttribute, "A job needs a handle function that does its work: fn handle() { ... }.", AddJobHandleFix(e));
            }
            OfferJobFailed(e, hasFailed);

            string jobName = e.GetAttributeValue("name")?.Trim();
            Scope jobScope = new Scope(inner, e.Span);
            if (jobName != null && declaredJobs.TryGetValue(jobName, out DeclaredJob declared) && declared.Element == e)
            {
                TypeShape instance = declared.Symbol.Shape.Instance;
                foreach (FieldInfo f in instance.Fields.Values.Where(f => !BaseJobMembers.Contains(f.Name)))
                {
                    DeclareImplicit(jobScope, f.Name, f.Shape, f.Description ?? $"The {f.Name} field.", e, f.Declaration);
                }
                DeclareImplicit(jobScope, "this", instance, $"The {jobName} job itself.", e);
            }
            DeclareImplicit(jobScope, "job", JobContextShape(), "The job being run: id, name, attempt, tries, queue, connection and dispatched_at.", e);
            WalkChildren(e.Children, jobScope, markupMode);
        }

        /// <summary>The shape of $job inside handle and failed.</summary>
        private static TypeShape JobContextShape()
        {
            TypeShape shape = new TypeShape(KType.Map);
            foreach ((string name, string type, string description) in new[]
            {
                ("id", KType.String, "The queued job's id (null when it runs with ->run())."),
                ("name", KType.String, "The job type's name."),
                ("attempt", KType.Number, "This attempt: 1 for the first."),
                ("tries", KType.Number, "How many attempts it gets."),
                ("queue", KType.String, "The queue it came from."),
                ("connection", KType.String, "The connection it came from."),
                ("dispatched_at", KType.Date, "When it was dispatched."),
            })
            {
                shape.Fields[name] = new FieldInfo(name, new TypeShape(type), null, description);
            }
            return shape;
        }

        /// <summary>
        /// <c>SendWelcome(user_id: 7)</c>: a call that makes a job. Its arguments go by name and must be the job's
        /// fields, and the fields without a default must be given.
        /// </summary>
        private bool AnalyzeJobCall(CallNode call, IdentifierNode id, ExpressionRegion region, Scope scope)
        {
            TextSpan span = region.Source.ToSource(id.Span);
            Symbol symbol = scope.Lookup(id.Name, span.Start);
            if (symbol?.Shape.Type != KType.JobType)
            {
                return false;
            }
            TextSpan callSpan = region.Source.ToSource(call.Span);
            if (call.Arguments.Any(a => a.Name == null))
            {
                Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount, $"{id.Name}() takes its fields by name, such as {id.Name}(id: 42).");
            }
            result.References.Add(new SymbolReference(symbol, span, false, false));
            List<string> fields = symbol.Shape.Instance.Fields.Keys.Where(f => !BaseJobMembers.Contains(f)).ToList();
            foreach (ArgumentNode arg in call.Arguments.Where(a => a.Name != null && !fields.Contains(a.Name)))
            {
                TextSpan nameSpan = region.Source.ToSource(arg.NameSpan);
                string close = Suggestions.Closest(arg.Name, fields);
                Report(nameSpan, DiagnosticSeverity.Error, DiagnosticCodes.UnknownParameter,
                    $"{id.Name} has no field '{arg.Name}'." + (close != null ? $" Did you mean {close}?" : fields.Count > 0 ? $" Its fields are: {string.Join(", ", fields)}." : $" {id.Name} has no fields."),
                    SuggestFix(nameSpan, arg.Name, fields, n => n));
            }
            if (declaredJobs.TryGetValue(id.Name, out DeclaredJob job) && job.Symbol == symbol)
            {
                List<string> missing = job.Required.Where(f => !call.Arguments.Any(a => a.Name == f)).ToList();
                if (missing.Count > 0)
                {
                    Report(callSpan, DiagnosticSeverity.Error, DiagnosticCodes.ArgumentCount,
                        $"{id.Name}() needs {string.Join(" and ", missing)}: {id.Name}({string.Join(", ", missing.Select(m => m + ": ..."))}).");
                }
            }
            return true;
        }

        /// <summary>Methods of jobs and chains work wherever a job is made, without importing KScript.Queue.</summary>
        private static bool IsJobValue(TypeShape shape) => shape.Type == KType.Job || shape.Type == KType.Chain;
    }
}
