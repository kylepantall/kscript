using System.IO;
using KScript.KScriptObjects;

namespace KScript.Arguments.FileHandlers
{
    class move : KScriptObject
    {
        public string from { get; set; }
        public string to { get; set; }


        public override bool Run()
        {

            FileAttributes attr = File.GetAttributes(HandleCommands(from));

            if (attr.HasFlag(FileAttributes.Directory))
            {
                Directory.Move(HandleCommands(from), HandleCommands(to));
                return true;
            }
            
            File.Move(HandleCommands(from), HandleCommands(to));
            return true;
        }

        public override string UsageInformation() => "Used to move files from a location (from), to a new location (to)";

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(KScript());
            validator.AddValidator(new KScriptValidationObject("to", false));
            //validator.AddValidator(new KScriptValidationObject("from", false, KScriptValidator.ExpectedInput.FileLocation));
            validator.Validate(this);
        }
    }
}
