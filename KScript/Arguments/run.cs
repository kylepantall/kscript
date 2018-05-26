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
        public override void Run()
        {
            if (string.IsNullOrWhiteSpace(args)) Process.Start(file);
            else Process.Start(file, args);
        }

        public override void Validate()
        {
            if (string.IsNullOrWhiteSpace(file)) throw new KScriptException("File must be provided");
        }
    }
}
