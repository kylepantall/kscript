using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class run : KScriptObject
    {
        public String file { get; set; }
        public string args { get; set; }
        public override bool Run()
        {
            if (string.IsNullOrWhiteSpace(HandleCommands(args)))
                Process.Start(HandleCommands(file));
            else Process.Start(HandleCommands(file), args);
            return true;
        }

        public override void Validate()
        {
            if (string.IsNullOrWhiteSpace(file)) throw new KScriptException("File must be provided");
        }

        public override string UsageInformation() => @"Used to launch a recognised path (directory, file, url, etc.) on the local machine.";
    }
}
