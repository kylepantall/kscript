namespace KScript.Commands
{
    public class find_file : KScriptCommand
    {
        public string directory;
        public string regex;

        public find_file(string directory, string regex)
        {
            this.directory = directory;
            this.regex = regex;
        }
        public override string Calculate()
        {
            return NULL;
        }
    }
}
