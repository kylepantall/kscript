using KScript.Metadata;
namespace KScript.Commands
{
    [KScriptDoc("The first file in a folder whose name matches a regular expression.")]
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


        public override void Validate() { }
    }
}
