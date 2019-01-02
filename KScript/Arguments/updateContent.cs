using KScript.KScriptObjects;

namespace KScript.Arguments
{
    class updateContent : KScriptObject
    {
        public string id { get; set; }


        public updateContent(string Contents) => this.Contents = Contents;

        public override bool Run()
        {
            KScriptObject obj = ParentContainer.GetObjectStorageContainer().GetObjectFromUID<KScriptObject>(id);
            obj.Contents = HandleCommands(Contents);
            return true;
        }

        public override string UsageInformation() => "Used to update the contents of an existing, registered KScript Object";

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("id", false));
            validator.Validate(this);
        }
    }
}
