using KScript.KScriptObjects;
using KScript.KScriptTypes.KScriptExceptions;
using System.IO;

namespace KScript.Arguments
{
    public class write : KScriptObject
    {
        /// <summary>
        /// Location to write to
        /// </summary>
        [KScriptProperty("The location to write to.", true)]
        [KScriptAcceptedOptions("C:\\file.txt\\", "D:\\Documents\\File.txt")]
        [KScriptExample("<write to=\"D:\\My file.txt\\\"> My new file Contents.</ write > ")]
        public string to { get; set; }

        public write(string Contents) => this.Contents = Contents;

        public override bool Run()
        {
            Contents = HandleCommands(Contents);
            to = HandleCommands(to);

            File.WriteAllText(to, Contents);
            return true;
        }

        public override string UsageInformation() => "Used to write Contents to a file using the properties ('Contents' - text) and ('to' - file path).";

        public override void Validate() => throw new KScriptNoValidationNeeded();
    }
}
