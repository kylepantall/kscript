namespace KScript.Arguments.KScriptHandlers
{
    public class x : KScriptObject
    {
        public string command { get; set; }

        public override bool Run()
        {
            switch (command.ToLower())
            {
                case "mutliarray_init_example_array":
                    MultiArray.MultiArrayParser.CreateExampleArray(ParentContainer);
                    break;
            }
            return true;
        }

        public override string UsageInformation()
        {
            return "Internal Use.";
        }

        public override void Validate()
        {
            throw new KScriptExceptions.KScriptNoValidationNeeded(this);
        }
    }
}
