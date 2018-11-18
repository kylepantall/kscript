using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;

namespace KScript.Commands
{
    public class acount : KScriptCommand
    {
        private readonly string id;
        public acount(string id) => this.id = id;
        public override string Calculate()
        {
            try
            {
                List<string> array = KScript().ArrayGet(id);

                if (array is null)
                {
                    throw new KScriptArrayNotFound(string.Format("Array with the ID '{0}' not found...", id));
                }
                else
                {
                    string count = KScript().ArrayGet(id).Count.ToString();
                    return count;
                }
            }
            catch (Exception ex)
            {
                HandleException(this, ex);
                return null;
            }
        }
    }
}
