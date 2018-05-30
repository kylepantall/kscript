using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class @ref : KScriptObject
    {
        public string directory { get; set; }
        public string @namespace { get; set; }
        public @ref() => RunImmediately = true;

        public override bool Run()
        {
            directory = HandleCommands(directory);
            @namespace = HandleCommands(@namespace);
            IEnumerable<string> files = Directory.EnumerateFiles(directory, "*.dll");
            foreach (var item in files)
                foreach (var t in Assembly.LoadFrom(item).GetTypes())
                    if (t.Namespace.ToLower() == @namespace.ToLower())
                        ParentContainer.AddKScriptObjectType(t);

            return true;
        }

        public override string UsageInformation() => @"Used to reference argument extensions for KScript.";
        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
