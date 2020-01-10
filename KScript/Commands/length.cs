namespace KScript.Commands
{
    public class length : KScriptCommand
    {
        private readonly string str = "";
        private readonly string match = Global.Values.NULL;
        public length(string str) => this.str = str;

        public length(string str, string match)
        {
            this.str = str;
            this.match = match;
        }
        public override string Calculate()
        {
            if (match == Global.Values.NULL)
            {
                return str.Length.ToString() ?? 0.ToString();
            }

            return ToBoolString(str.Length.ToString() == match);
        }
        public override void Validate() { }
    }
}
