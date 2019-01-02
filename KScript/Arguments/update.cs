using System;
using KScript.KScriptObjects;

namespace KScript.Arguments
{
    public class update : KScriptObject
    {
        [KScriptProperty("Used to identify the def to update.", true)]
        [KScriptAcceptedOptions("$[id]")]
        [KScriptExample("<update to=\"example\"> New value </update>")]
        public string to { get; set; }


        [KScriptProperty("Used to define what type of update should occur. Default: replace", false)]
        [KScriptAcceptedOptions("append", "replace")]
        public string type
        {
            get { return Enum.GetName(typeof(types), _type); }
            set { _type = (types)Enum.Parse(typeof(types), value); }
        }

        public update(string Contents)
        {
            ValidationType = ValidationTypes.BEFORE_PARSING;
            this.Contents = Contents;
        }

        private types _type = types.replace;
        private enum types { replace, append }

        public override bool Run()
        {
            if (_type == types.replace)
            {
                Def(to).Contents = HandleCommands(Contents);
            }
            else
            {
                string tmp = Def(to).Contents;
                tmp = tmp + HandleCommands(Contents);
                Def(to).Contents = tmp;
            }
            return true;
        }

        public override string UsageInformation() => "Used to update the value stored within a def.";

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("type", false, "replace", "append"));
            validator.AddValidator(new KScriptValidationObject("to", false, KScriptValidator.ExpectedInput.DefID));
            validator.Validate(this);
        }
    }
}
