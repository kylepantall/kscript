using System.Collections.Generic;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class array : KScriptObject
    {
        [KScriptObjects.KScriptProperty("The unique ID of the array", true)]
        public string id { get; set; }

        [KScriptObjects.KScriptProperty("Property used to define where to create the array from.", false)]
        public string from { get; set; }


        public override bool Run()
        {
            List<string> array = new List<string>();
            KScript().ArrayInsert(id, array);

            bool createUsingExisting = !string.IsNullOrEmpty(from);

            if (createUsingExisting)
            {
                string val = HandleCommands(from);

                if (ParentContainer.ArraysGet().ContainsKey(val))
                {
                    KScript().ArraysGet()[id] = KScript().ArraysGet()[val];
                }
                else
                {

                }
            }

            return true;
        }

        public override string UsageInformation() => "Used to declare array's with specified ID.";
        public override void Validate()
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new KScriptMissingAttribute(this);
            }
            else
            {
                throw new KScriptNoValidationNeeded(this);
            }
        }
    }
}
