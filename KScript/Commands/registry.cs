using Microsoft.Win32;

namespace KScript.Commands
{
    public class registry : KScriptCommand
    {
        private string path { get; set; }
        private string key { get; set; }

        public registry(string path, string key) { this.path = path; this.key = key; }
        public override string Calculate()
        {
            try
            {
                return Registry.GetValue(path, key, string.Empty).ToString();
            }
            catch (System.Exception ex)
            {
                HandleException(ex, this);
                return null;
            }
        }


        public override void Validate() { }
    }
}
