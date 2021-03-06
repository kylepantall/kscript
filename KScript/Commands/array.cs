﻿using System;

namespace KScript.Commands
{
    public class array : KScriptCommand
    {
        private readonly string id, index;
        public array(string id, string index)
        {
            this.id = id;
            this.index = index;
        }

        public override string Calculate()
        {
            try
            {
                return KScript().ArrayGet(this, id)[int.Parse(index)];
            }
            catch (Exception ex)
            {
                HandleException(ex, this);
                return null;
            }
        }

        public override void Validate() { }
    }
}
