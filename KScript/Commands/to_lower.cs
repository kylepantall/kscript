namespace KScript.Commands
{
    public class to_lower : KScriptCommand
    {
        private string str;
        public to_lower(string str) => this.str = str;

        public override string Calculate() => str.ToLower();
    }
}
