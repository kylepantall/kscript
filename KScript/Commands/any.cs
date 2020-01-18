using System.Collections.Generic;
using System.Linq;
using KScript.Handlers;

namespace KScript.Commands
{
    public class any : KScriptCommand
    {
        private readonly string[] conditions;
        public any(params string[] conditions) => this.conditions = conditions;

        public override string Calculate()
        {
            IEnumerable<string> tmp = conditions.ToArray().Select(i => KScriptCommandHandler.HandleCommands(i, ParentContainer, GetBaseObject()));

            if (tmp.All(i => KScriptBoolHandler.IsBool(i)))
            {
                return ToBoolString(tmp.Any(x => KScriptBoolHandler.Convert(x)));
            }
            else
            {
                throw new KScriptExceptions.KScriptBoolInvalid(this, "Conditional statements are not all of type bool");
            }
        }

        public override void Validate() { }
    }
}
