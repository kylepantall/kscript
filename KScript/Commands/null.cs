﻿using KScript.Handlers;

namespace KScript.Commands
{
    public class @null : KScriptCommand
    {
        private readonly string value = "";
        public @null(string val) => value = val;

        public override string Calculate()
        {
            if (string.IsNullOrEmpty(value))
            {
                return ToBoolString(KScriptCommandHandler.HandleCommands(value, KScript()) == NULL);
            }
            else
            {
                return NULL;
            }
        }
    }
}