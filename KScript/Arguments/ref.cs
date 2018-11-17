using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects]
    public class @ref : KScriptObject
    {
        [KScriptObjects.KScriptProperty("The directory to look for extensions for KScript.", true)]
        public string directory { get; set; }

        [KScriptObjects.KScriptProperty("The namespace to use when loading the extensions.", false)]
        public string @namespace { get; set; }

        public @ref() => RunImmediately = true;

        public override bool Run()
        {
            directory = HandleCommands(directory);
            @namespace = HandleCommands(@namespace);
            IEnumerable<string> files = Directory.EnumerateFiles(directory, "*.dll");
            foreach (var item in files)
            {
                Type[] types = (from t in Assembly.LoadFrom(item).GetTypes() where typeof(KScriptObject).IsAssignableFrom(t) select t).ToArray();
                foreach (Type t in types)
                {
                    if (string.IsNullOrEmpty(@namespace))
                    {
                        ParentContainer.AddKScriptObjectType(t);
                    }
                    else if (t.Namespace.ToLower() == @namespace.ToLower())
                    {
                        ParentContainer.AddKScriptObjectType(t);
                    }
                }
            }

            return true;
        }

        public override string UsageInformation() => @"Used to reference argument extensions for KScript.";
        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
