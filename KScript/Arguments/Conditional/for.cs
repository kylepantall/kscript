using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    [KScriptDoc("Loops over a list, a map or a range such as 0..10 (end exclusive) or 0..100 step 5.", Example = "<for each=\"p\" in=\"$people\">\n  <echo>{$p.name}</echo>\n</for>")]
    [KScriptElement(KScriptContentKind.Statements, IsLoop = true)]
    [KScriptAttribute("each", KScriptValueKind.DeclareVariables, "The loop variable, or key, value for maps.", Required = true)]
    [KScriptAttribute("in", KScriptValueKind.Expression, "What to loop over.", Required = true)]
    [KScriptAttribute("index", KScriptValueKind.DeclareVariable, "A variable that counts from 0.")]
    public class @for : KScriptObjectLoop
    {
        public override bool Run() => true;
        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
