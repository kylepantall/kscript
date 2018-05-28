using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class k : KScriptCommand
    {
        public string value;

        public k(string value) => this.value = value;
        public k() => value = string.Empty;


        public override string Calculate()
        {
            string val = value.ToLower();

            switch (val)
            {
                case "os.version": return Environment.OSVersion.VersionString;
                case "machine.name": return Environment.MachineName;
                case "execution_file": return ParentContainer.FilePath;
                case "execution_directory": return ParentContainer.FileDirectory;
                default:
                    return ParentContainer.FileDirectory;
            }
        }
    }
}
