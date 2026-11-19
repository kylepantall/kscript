using KScript.Metadata;
using KScript.KScriptExceptions;

namespace KScript
{
    public class KScriptConditional : KScriptObject
    {
        [KScriptRetired("Use test=\"...\" with an expression.", Rename = "test")]
        public string condition { get; set; } = "";

        /// <summary>
        /// Evaluates the condition. Child objects only run when this returns true.
        /// </summary>
        public virtual bool EvaluateCondition() => condition != null && ToBool(HandleCommands(condition));

        public override bool Run()
        {
            bool result = EvaluateCondition();
            GetValueStore()["result"] = result;
            KScript()?.SetLastConditionResult(GetBaseObject(), result);
            return result;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to store an array of KScriptObjects.";
    }
}
