using KScript.KScriptObjects;
using KScript.KScriptTypes.KScriptExceptions;

namespace KScript.Arguments
{
    public class defm : KScriptObject
    {
        [KScriptProperty("The unique ids for several definitions (id's seperated by commas). Must not contain any spaces or $ symbols.", true)]
        [KScriptExample("<defm ids=\"tmp_str,tmp_name\"> ... </defm>")]
        [KScriptExample("<defm ids=\"username,name,password\"> ... </defm>")]
        [KScriptExample("<defm ids=\"email_address,phonenumber,age\"> ... </defm>")]
        public string ids { get; set; }

        public defm(string Contents) => this.Contents = Contents;

        public override bool Run()
        {
            if (string.IsNullOrWhiteSpace(Contents))
            {
                throw new KScriptNoRunMethodImplemented();
            }
            else { Contents = HandleCommands(Contents); }
            return true;
        }

        public override string UsageInformation() => @"Used to store values to the definition log within KScript." +
            "\nThe id attribute is to within other KScriptObjects to retrieve the values." +
            "\nE.g. '$tmp_name' can be used to retrieve the value of a def KScriptObject with the id 'tmp_name'." +
            "\nUsed to declare multiple defs";

        public override void Validate()
        {
            if (ids.Contains(" ") || ids.Contains("$"))
            {
                throw new KScriptException("The id cannot contain any spaces or $ symbols.");
            }
        }
    }
}
