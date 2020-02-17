using KScript.Handlers;
using System.Collections.Generic;
using System.Linq;

namespace KScript.Commands
{
    public class and : KScriptCommand
    {
        private readonly string[] conditions;
        public and(params string[] conditions) => this.conditions = conditions;

        public override string Calculate()
        {
            IEnumerable<string> tmp = conditions.ToArray().Select(i => KScriptCommandHandler.HandleCommands(i, KScript(), GetBaseObject()));

            if (tmp.All(i => KScriptBoolHandler.IsBool(i)))
            {
                return ToBoolString(tmp.All(x => KScriptBoolHandler.Convert(x)));
            }
            else
            {
                throw new KScriptExceptions.KScriptBoolInvalid(this, "Conditional statements are not all of type bool");
            }
        }

        public override void Validate() { }
    }
}
