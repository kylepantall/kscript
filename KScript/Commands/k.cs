using KScript.KScriptExceptions;
using System;
using System.Runtime.InteropServices;

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
                throw new KScriptValidationFail(this, "Value cannot be null");
            }
            else
            {
                switch (value.ToLower())
                {
                    case "os.name": return GetOSFriendlyName();
                    case "machine.name": return System.Environment.MachineName;
                    case "script.path": return KScript().FilePath;
                    case "script.directory": return KScript().FileDirectory;
                    case "username": return System.Environment.UserName;
                    case "math.pi": return Math.PI.ToString();
                    case "math.e": return Math.E.ToString();
                    case Global.Values.NULL: return NULL;
                    default: return KScript().FileDirectory;
                }
            }
        }

        /// <summary>
        /// Returns the name of the operating system.
        /// </summary>
        /// <returns>OS Name</returns>
        public static string GetOSFriendlyName() => RuntimeInformation.OSDescription;


        public override void Validate() { }

    }
}
