using KScript.KScriptObjects;
using KScript.KScriptExceptions;

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
                throw new KScriptNoRunMethodImplemented(this);
            }
            else { Contents = HandleCommands(Contents); }
            return true;
        }

        public override string UsageInformation() => @"Used to declare multiple variables within the KScript Definition container." +
            "\nMultiple KScript defs can be declared by using a commas, e.g. id,id2,id3 would define id, id2 and id3 with the same value." +
            "\nThe id attribute is used within other KScriptObjects to retrieve the value of the declared definition." +
            "\nE.g. '$tmp_name' can be used to retrieve the value of the def with the id 'tmp_name'.";

        public override void Validate()
        {
            if (ids.Contains(" ") || ids.Contains("$"))
            {
                throw new KScriptValidationFail(this, "The id cannot contain any spaces or $ symbols.");
            }
        }
    }
}
