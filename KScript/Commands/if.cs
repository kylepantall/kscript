namespace KScript.Commands
{
    class @if : KScriptCommand
    {

        private readonly string condition;
        private readonly string command;

        public @if(string value)
        {
            if (string.IsNullOrEmpty(value) || value == Global.Values.NULL)
            {
                condition = ToBoolString(false);
            }
            else
            {
                condition = ToBoolString(true);
            }
        }

        public @if(string condition, string command)
        {
            this.condition = condition;
            this.command = command;
        }

        public override string Calculate()
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                if (ToBool(condition))
                {
                    return ReturnFormattedVariables(command);
                }
                else
                {
                    return string.Empty;
                }
            }
            else
            {
                return condition;
            }
        }

        public override void Validate() { }
    }
}
