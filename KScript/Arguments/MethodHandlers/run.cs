using KScript.Metadata;
using KScript.KScriptExceptions;
using System.Diagnostics;

namespace KScript.Arguments
{
    [KScriptObjects.KScriptNoInnerObjects]
    [KScriptRetired("Use <fn> and @name(), or <call fn=\"...\">.")]
    public class run : KScriptObject
    {
        [KScriptObjects.KScriptProperty("The location of the process to run.")]
        [KScriptObjects.KScriptExample("file path")]
        [KScriptObjects.KScriptExample("url")]
        [KScriptObjects.KScriptExample("directory")]
        public string file { get; set; }

        [KScriptObjects.KScriptProperty("The arguments to pass to the process.")]
        public string args { get; set; }


        public run() { }

        public override bool Run()
        {
            // Uses the platform's default handler, so files, directories and URLs open on Windows, Linux and macOS.
            string target = Helpers.Platform.ResolvePath(HandleCommands(file), KScript().GetFileDirectory());
            Helpers.Platform.Open(target, HandleCommands(args ?? string.Empty));

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
