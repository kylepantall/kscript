using System;
using System.Collections.Generic;
using KScript.Handlers;
using KScript.KScriptExceptions;

namespace KScript.Arguments
{
    public class array : KScriptObject
    {
        public array(string Contents) { this.Contents = Contents; }

        [KScriptObjects.KScriptProperty("The unique ID of the array", true)]
        public string id { get; set; }

        [KScriptObjects.KScriptProperty("Property used to define where to create the array from.", false)]
        public string from { get; set; }

        [KScriptObjects.KScriptProperty("Used to define how the array string should be split", false)]
        public string delimiter { get; set; } = ",";


        public override bool Run()
        {
            List<string> array = new List<string>();
            KScript().ArrayInsert(id, array);

            bool createUsingExisting = !string.IsNullOrEmpty(from);

            if (createUsingExisting)
            {
                string val = HandleCommands(from);

                if (!ParentContainer.ArraysGet().ContainsKey(val))
                {
                    throw new KScriptArrayNotFound(this,
                        string.Format("The array '{0}' was not found.", val));
                }

                KScript().ArraysGet()[id] = KScript().ArraysGet()[val];
            }


            try
            {
                if (!string.IsNullOrEmpty(Contents))
                {
                    string split_contents = HandleCommands(Contents);
                    KScriptArraySplitHandler.Split(split_contents, string.IsNullOrEmpty(delimiter) ? "," : delimiter).ForEach(value => KScript().ArrayGet(this, id).Add(value));
                }
            }
            catch (Exception)
            {
                throw new KScriptException("Failture to create array using contents. Ensure values are seperated using ','");
            }

            return true;
        }

        public override string UsageInformation() => "Used to declare array's with specified ID.";
        public override void Validate()
        {
            if (!string.IsNullOrEmpty(id))
            {
                throw new KScriptNoValidationNeeded(this);
            }

            throw new KScriptMissingAttribute(this);
        }
    }
}
