using KScript.KScriptObjects;

namespace KScript.Commands
{
    public class constant : KScriptCommand
    {
        private readonly string id;

        public constant(string id) => this.id = id;

        public override string Calculate() => ParentContainer.GetConstantProperties()[id];

        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("id", false));
            validator.Validate(this);
        }
    }
}
