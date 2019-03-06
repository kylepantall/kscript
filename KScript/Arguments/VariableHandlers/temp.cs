using KScript.KScriptObjects;

namespace KScript.Arguments
{
    class temp : KScriptObject
    {
        public temp(string contents) : base(contents) { }

        public string id { get; set; }

        public override bool Run()
        {
            if (ParentContainer.GetDefs().ContainsKey(id))
            {
                ParentContainer.RemoveDef(id);
            }

            ParentContainer.AddDef(id, new def(Contents) { id = id });
            return true;
        }

        public override string UsageInformation()
        {
            return "Used to declare, and re-declare a variable. Used in temporary situations - such a for loops";
        }

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("id", false));
            validator.Validate(this);
        }
    }
}
