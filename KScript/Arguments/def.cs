using KScript.KScriptObjects;
using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Arguments
{
    public class def : KScriptObject
    {
        [KScriptProperty("The unique id for this definition. Must not contain any spaces or $ symbols.", true)]
        [KScriptExample("<def id=\"tmp_str\"> ... </def>")]
        [KScriptExample("<def id=\"username\"> ... </def>")]
        [KScriptExample("<def id=\"email_address\"> ... </def>")]
        public string id { get; set; }
        public def(string Contents) => this.Contents = Contents;
        public override bool Run()
        {
            if (string.IsNullOrWhiteSpace(Contents))
            {
                throw new KScriptNoRunMethodImplemented();
            }
            else { Contents = HandleCommands(Contents); }
            return true;
        }
        public override void Validate()
        {
            if (id.Contains(" ") || id.Contains("$"))
            {
                throw new KScriptException("The id cannot contain any spaces or $ symbols.");
            }
        }

        public override string UsageInformation() => @"Used to store values to the definition log within KScript." +
            "\nThe id attribute is to within other KScriptObjects to retrieve the values." +
            "\nE.g. '$tmp_name' can be used to retrieve the value of a def KScriptObject with the id 'tmp_name'.";
    }
}
