﻿using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Arguments
{
    public class ainsert : KScriptObject
    {
        public ainsert(string contents) => Contents = contents;
        public string to { get; set; }

        public override bool Run()
        {
            if (KScript().ArrayGet(to) != null)
            {
                KScript().ArrayGet(to).Add(HandleCommands(Contents));
            }
            else
            {
                throw new KScriptArrayNotFound(string.Format("The array with the ID '{0}' was not found.", to));
            }
            return true;
        }

        public override string UsageInformation() => "Used to insert into an Array with specified ID.";
        public override void Validate() => new KScriptNoValidationNeeded();
    }
}
