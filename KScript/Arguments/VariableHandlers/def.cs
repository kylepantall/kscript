using KScript.KScriptExceptions;
using KScript.KScriptObjects;

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
        public def() => Contents = NULL;

        public override bool Run()
        {
            if (string.IsNullOrWhiteSpace(Contents))
            {
                throw new KScriptNoRunMethodImplemented(this);
            }

            else { ParentContainer[id].Contents = HandleCommands(Contents); }
            return true;
        }

        public override void Validate()
        {
            if (id.Contains(" ") || id.Contains("$"))
            {
                throw new KScriptValidationFail(this, "The id cannot contain any spaces or $ symbols.");
            }
        }

        public override string UsageInformation() => @"Used to declare a variable within the KScript Definition container." +
            "\nThe id attribute is used within other KScriptObjects to retrieve the value of the declared definition." +
            "\nE.g. '$tmp_name' can be used to retrieve the value of the def with the id 'tmp_name'.";
    }
}
