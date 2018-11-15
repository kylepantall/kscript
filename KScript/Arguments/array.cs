using KScript.KScriptTypes.KScriptExceptions;
using System.Collections.Generic;

namespace KScript.Arguments
{
    public class array : KScriptObject
    {
        public string id { get; set; }

        public override bool Run()
        {
            KScript().ArrayInsert(id, new List<string>());
            return true;
        }

        public override string UsageInformation() => "Used to declare array's with specified ID.";
        public override void Validate()
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new KScriptException("No ID has been specified for the array object.");
            }
            else
            {
                throw new KScriptNoValidationNeeded();
            }
        }
    }
}
