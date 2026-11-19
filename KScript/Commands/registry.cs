using KScript.Metadata;
using Microsoft.Win32;

namespace KScript.Commands
{
    [KScriptDoc("Reads a registry value (Windows only).")]
    public class registry : KScriptCommand
    {
        private string path { get; set; }
        private string key { get; set; }

        public registry(string path, string key) { this.path = path; this.key = key; }
        public override string Calculate()
        {
            if (!System.OperatingSystem.IsWindows())
            {
                throw new KScriptExceptions.KScriptException(this, "@registry is only supported on Windows.");
            }

            try
            {
                return Registry.GetValue(path, key, string.Empty)?.ToString() ?? NULL;
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
