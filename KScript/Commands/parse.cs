using KScript.Handlers;

namespace KScript.Commands
{
    /// <summary>
    /// The @parse command allows the contents 
    /// </summary>
    public class parse : KScriptCommand
    {
        /// <summary>
        /// Property to store constructor value.
        /// </summary>
        private readonly string value = string.Empty;

        /// <summary>
        /// Constructor for the parse command.
        /// </summary>
        /// <param name="value"></param>
        public parse(string value) => this.value = value;


        /// <summary>
        /// Returns the formatted string using variables.
        /// </summary>
        /// <returns>Variable formatted string</returns>
        public override string Calculate()
        {
            string val = value;
            val = KScriptCommandHandler.HandleCommands(val, KScript(), this, true);
            return ReturnFormattedVariables(val);
        }

        public override void Validate() { }
    }
}
