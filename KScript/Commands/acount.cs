using System;
using System.Collections.Generic;
using KScript.KScriptExceptions;

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
                List<string> array = KScript().ArrayGet(this, id);

                if (array is null)
                {
                    throw new KScriptArrayNotFound(this);
                }
                else
                {
                    string count = KScript().ArrayGet(this, id).Count.ToString();
                    return count;
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, this);
                return null;
            }
        }
    }
}
