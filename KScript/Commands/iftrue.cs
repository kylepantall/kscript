namespace KScript.Commands
{
    /// <summary>
    /// Returns the result, only if the condition is true
    /// </summary>
    public class iftrue : KScriptCommand
    {
        private readonly string condition;
        private readonly string @return;

        public iftrue(string condition, string @return)
        {
            this.condition = condition;
            this.@return = @return;
        }

        public override string Calculate()
        {
            if (ToBool(condition))
            {
                return @return;
            }
            return string.Empty;
        }

        public override void Validate()
        {

        }
    }
}
