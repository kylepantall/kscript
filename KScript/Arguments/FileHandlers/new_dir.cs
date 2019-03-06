using System.IO;
using KScript.KScriptObjects;

namespace KScript.Arguments.FileHandlers
{
    class new_dir : KScriptObject
    {
        public string path { get; set; }

        public override bool Run()
        {
            Directory.CreateDirectory(HandleCommands(path));
            return true;
        }

        public override string UsageInformation() => "Creates a directory with the path given";

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("path", false));
            validator.Validate(this);
        }
    }
}
