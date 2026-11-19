using KScript.Metadata;
using System.Linq;

namespace KScript.Commands
{
    [KScriptRetired("Variables are checked before the script runs; use ?? for values that may be null.")]
    public class exists : KScriptCommand
    {
        private readonly string id;
        public exists(string id) => this.id = id;
        public override string Calculate() => ToBoolString(KScript().GetDefs().Any(i => i.Key == id));

        public override void Validate() { }
    }
}
