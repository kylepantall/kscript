using KScript.KScriptExceptions;
using System;
using System.Diagnostics;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects]
    public class run : KScriptObject
    {
        [KScriptObjects.KScriptProperty("The location of the process to run.")]
        [KScriptObjects.KScriptExample("file path")]
        [KScriptObjects.KScriptExample("url")]
        [KScriptObjects.KScriptExample("directory")]
        public String file { get; set; }

        [KScriptObjects.KScriptProperty("The arguments to pass to the process.")]
        public string args { get; set; }

        public override bool Run()
        {
            if (string.IsNullOrWhiteSpace(HandleCommands(args)))
            {
                Process.Start(HandleCommands(file));
            }
            else
            {
                Process.Start(HandleCommands(file), args);
            }

            return true;
        }

        public override void Validate()
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                throw new KScriptException("FileNotFound", "File must be provided");
            }
        }

        public override string UsageInformation() => @"Used to launch a recognised path (directory, file, url, etc.) on the local machine.";
    }
}
