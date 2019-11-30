using System.Collections.Generic;
using System.Linq;
using KScript.Handlers;

namespace KScript.Commands
{
    public class @or : KScriptCommand
    {
        private readonly string[] conditions;
        public @or(params string[] conditions) => this.conditions = conditions;

        public override string Calculate()
        {
            IEnumerable<string> tmp = conditions.ToArray().Select(i => KScriptCommandHandler.HandleCommands(i, ParentContainer, GetBaseObject()));

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
