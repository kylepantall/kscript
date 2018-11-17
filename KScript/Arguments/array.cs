using KScript.KScriptTypes.KScriptExceptions;
using System.Collections.Generic;

namespace KScript.Arguments
{
    public class array : KScriptObject
    {
        [KScriptObjects.KScriptProperty("The unique ID of the array", true)]
        public string id { get; set; }

        public override bool Run()
        {
            List<string> array = new List<string>();
            KScript().ArrayInsert(id, array);
            return true;
        }

        public override string UsageInformation() => "Used to declare array's with specified ID.";
        public override void Validate()
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new KScriptMissingAttribute("No ID has been specified for the array object.");
            }
            else
            {
                throw new KScriptNoValidationNeeded();
            }
        }
    }
}
