using KScript.KScriptTypes.KScriptExceptions;
using System;

namespace KScript.Commands
{
    public class k : KScriptCommand
    {
        public string value;

        public k(string value) => this.value = value;
        public k() => value = string.Empty;


        public override string Calculate()
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new KScriptValidationFail("Value cannot be null");
            }
            else
            {
                switch (value.ToLower())
                {
                    case "os.version": return Environment.OSVersion.VersionString;
                    case "machine.name": return Environment.MachineName;
                    case "script.path": return ParentContainer.FilePath;
                    case "script.directory": return ParentContainer.FileDirectory;
                    case "username": return Environment.UserName;
                    case "math.pi": return Math.PI.ToString();
                    case "math.e": return Math.E.ToString();
                    case "null": return NULL;
                    default: return ParentContainer.FileDirectory;
                }
            }
        }
    }
}
