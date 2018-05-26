using KScript.Handlers;
using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class input : KScriptObject
    {
        public string contents { get; set; }

        /// <summary>
        /// Type of input to expect...
        /// 
        /// - Email (to be implemented)
        /// - Phone number (to be implemented)
        /// - string (by default)
        /// </summary>
        public string type { get; set; }

        /// <summary>
        /// Where to save input to - uses "def" to save input.
        /// </summary>
        public string to { get; set; }


        /// <summary>
        /// Clear-After-Input command;
        /// </summary>
        public string cai { get; set; } = "no";

        public input(string content) => contents = content;

        public override void Run()
        {
            if (string.IsNullOrWhiteSpace(type)) type = "string";
            Out(contents);

            if (Def(to) == null) throw new KScriptException(string.Format("Definition '{0}' has not been declared", to));
            switch (type.ToLower())
            {
                case "number":
                    ParentContainer[to].contents = InNumber().ToString();
                    break;
                case "multiline":
                    ParentContainer[to].contents = In();
                    break;
                default:
                    ParentContainer[to].contents = In();
                    break;
            }
            if (ParentContainer.Properties.ClearAfterInput || KScriptBoolHandler.Convert(cai)) Console.Clear();
        }

        public override void Validate()
        {
            if (type != "string" && type != "number" && string.IsNullOrWhiteSpace(type))
            {
                throw new KScriptInvalidScriptType();
            }
        }
    }
}
