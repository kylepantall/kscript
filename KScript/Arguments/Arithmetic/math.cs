using KScript.Handlers;
using KScript.KScriptObjects;

namespace KScript.Arguments.Arithmetic
{
    class math : KScriptObject
    {
        public math(string math) : base(math) { }

        [KScriptProperty("Used to specify which variable to store result in. Alternatively, you can retrieve the last value" +
            "of the math command using @math_previous()", false)]
        public string to { get; set; }


        public override bool Run()
        {
            string math = HandleCommands(Contents);
            string result = KScriptArithmeticHandler.HandleCalculation(math);

            if (!string.IsNullOrWhiteSpace(to))
            {
                Def(to).Contents = result;
            }

            ParentContainer.AddGlobalValue("math", "previous_result", result);

            return true;
        }

        public override string UsageInformation()
        {
            return "Returns the mathematical result of an expressed equation.";
        }


        public override void Validate()
        {
            KScriptValidator validator = new KScriptValidator(ParentContainer);
            validator.AddValidator(new KScriptValidationObject("to", true, KScriptValidator.ExpectedInput.DefID));
            //validator.Validate(this);
        }
    }
}
