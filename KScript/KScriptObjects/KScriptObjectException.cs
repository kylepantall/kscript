using KScript.KScriptExceptions;
using System.Collections.Generic;

namespace KScript.KScriptObjects
{
    public class KScriptObjectException : KScriptObject
    {
        public KScriptObjectException() => Children = new List<KScriptObject>();
        public List<KScriptObject> Children { get; set; }

        public string exception { get; set; }

        public override bool Run()
        {
            if (!Ignore && KScript().AllowExecution)
            {
                foreach (KScriptObject item in Children)
                {
                    if (item != null)
                    {
                        item.Init(KScript());
                        item.Validate();
                        item.Run();
                    }
                }
            }
            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded(this);
        public override string UsageInformation() => @"Used to store an array of KScriptObjects.";
    }
}
