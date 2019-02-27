using System.Collections.Generic;

namespace KScript.Arguments
{
    public class languages : KScriptObject
    {
        [KScriptObjects.KScriptProperty("Used to define the prefix for variables", false)]
        public string prefix { get; set; }


        private Dictionary<string, List<KeyValuePair<string, string>>> Values;

        public override bool Run()
        {
            Values = (Dictionary<string, List<KeyValuePair<string, string>>>)GetValueStore()["data"];
            string Language = KScript().Properties.Language;

            List<KeyValuePair<string, string>> values = (!Values.ContainsKey(Language) ?
                Values["default"] : Values[KScript().Properties.Language]);

            foreach (var item in values)
            {
                KScript().AddDef(string.IsNullOrWhiteSpace(prefix) ?
                    item.Key : prefix + item.Key,
                    new def(item.Value));
            }

            return true;
        }

        public override string UsageInformation() => "Used to define language dependent variables.";

        public override void Validate() { }
    }
}
