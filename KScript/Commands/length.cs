namespace KScript.Commands
{
    public class length : KScriptCommand
    {
        private string str = "";
        public length(string str) => this.str = str;
        public override string Calculate() => str.Length.ToString() ?? 0.ToString();
        public override void Validate() { }
    }
}
