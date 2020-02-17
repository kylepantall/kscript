using KScript.MultiArray;
using System.Collections.Generic;

namespace KScript.Arguments.FileHandlers
{
    public class file_info : KScriptObject
    {
        [KScriptObjects.KScriptProperty("The file to obtain the information for.", true)]
        public string path { get; set; }


        [KScriptObjects.KScriptProperty("The ID of the array to store the returned values. If no array is defined" +
            " then ~this[] is used.", false)]
        public string to { get; set; }

        public override bool Run()
        {
            bool array_defined = !string.IsNullOrWhiteSpace(to);

            if (!System.IO.File.Exists(HandleCommands(path)))
            {
                throw new KScriptExceptions.KScriptException(this, "File does not exist.");
            }

            System.IO.FileInfo x = new System.IO.FileInfo(HandleCommands(path));

            ArrayBase values = new ArrayBase(
               new ArrayCollection(new List<IArray>() {
                   new ArrayItem("Directory", x.Directory.FullName),
                   new ArrayItem("Extension", x.Extension),
                   new ArrayItem("Size", x.Length.ToString()),
                   new ArrayItem("Name", x.Name),
                   new ArrayItem("ReadOnly", ToBoolString(x.IsReadOnly)),
                   new ArrayItem("LastWriteTime", x.LastWriteTimeUtc.ToString()),
                   new ArrayItem("LastAccessTime", x.LastAccessTimeUtc.ToString())
               })
           );

            KScript().AddMultidimensionalArray(array_defined ? to : "this", values);

            return true;
        }

        public override string UsageInformation() => "Used to return the associated information for the given file.";

        public override void Validate()
        {
            throw new KScriptExceptions.KScriptNoValidationNeeded(this);
        }
    }
}
