using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    /// <summary>
    /// Runs its inner objects when the preceding sibling &lt;if&gt; evaluated to false.
    /// <code>
    /// &lt;if condition="..."&gt; ... &lt;/if&gt;
    /// &lt;else&gt; ... &lt;/else&gt;
    /// </code>
    /// </summary>
    [KScriptDoc("Runs when the earlier <if>/<elif> tests were all false.")]
    [KScriptElement(KScriptContentKind.Statements, MustFollow = "if, elif, for")]
    public class @else : KScriptObject
    {
        public override bool Run()
        {
            bool? ifResult = KScript().GetLastConditionResult(GetBaseObject());

            if (ifResult == null)
            {
                throw new KScriptException(this, "<else> must directly follow an <if> element.");
            }

            return !ifResult.Value;
        }

        public override string UsageInformation() => "Used to only run the inner KScriptObjects if the preceding <if> condition was false.";

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
    }
}
