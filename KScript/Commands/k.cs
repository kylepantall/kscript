using System;
using System.Management;
using KScript.KScriptExceptions;

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

        /// <summary>
        /// Returns the name of the operating system.
        /// </summary>
        /// <returns>OS Name</returns>
        public static string GetOSFriendlyName()
        {
            string result = string.Empty;
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                foreach (ManagementObject os in searcher.Get())
                {
                    result = os["Caption"].ToString();
                    break;
                }
            }
            catch (Exception) { }
            return result;
        }


        public override void Validate() { }

    }
}
