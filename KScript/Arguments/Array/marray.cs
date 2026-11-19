using KScript.Metadata;
using KScript.KScriptObjects;

namespace KScript.Arguments.Array
{
    [KScriptDoc("A readable form for big nested data: nested <item key> elements become a map or list value.")]
    [KScriptElement(KScriptContentKind.Statements)]
    [KScriptAttribute("id", KScriptValueKind.DeclareVariable, "The variable to declare.", Required = true)]
    class marray : KScriptObject
    {
        [KScriptProperty(
            @"Used as a unique identifier for this array.",
            false
        )]
        public string id { get; set; }


        [KScriptProperty(
            @"Used to define a closure to call before each insertion.",
            false
        )]
        [KScriptRetired("Closures on arrays were removed in KScript 2. Use list methods with fn() lambdas, such as $list->map(fn($x) => ...).")]
        public string _insertion { get; set; }

        [KScriptProperty(
            @"Used to define a closure to call before each removal.",
            false
        )]
        [KScriptRetired("Closures on arrays were removed in KScript 2. Use list methods with fn() lambdas, such as $list->map(fn($x) => ...).")]
        public string _removal { get; set; }


        public marray(string Contents) => this.Contents = Contents;
        public override bool Run() => true;
        public override string UsageInformation() => @"Used to add a new MultiArray for this script with the given unique name";
        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(KScript());
            validator.AddValidator(new KScriptValidationObject("id", false));
            validator.Validate(this);
        }
    }
}
