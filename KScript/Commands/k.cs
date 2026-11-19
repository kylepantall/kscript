using KScript.Metadata;
using KScript.KScriptExceptions;
using System;
using System.Runtime.InteropServices;

namespace KScript.Commands
{
    [KScriptDoc("A value about the environment: os.name, os.platform, path.separator, newline, user.home, temp.directory, machine.name, script.path, script.directory, username, math.pi, math.e.")]
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
                    case "os.platform": return GetPlatform();
                    case "path.separator": return System.IO.Path.DirectorySeparatorChar.ToString();
                    case "newline": return System.Environment.NewLine;
                    case "user.home": return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                    case "temp.directory": return System.IO.Path.GetTempPath();
                    case "machine.name": return System.Environment.MachineName;
                    case "script.path": return KScript().FilePath;
                    case "script.directory": return KScript().FileDirectory;
                    case "username": return System.Environment.UserName;
                    case "math.pi": return Math.PI.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case "math.e": return Math.E.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        /// <summary>
        /// Returns a short platform identifier: windows, linux, macos, freebsd or unknown.
        /// </summary>
        public static string GetPlatform()
        {
            if (OperatingSystem.IsWindows()) return "windows";
            if (OperatingSystem.IsMacOS()) return "macos";
            if (OperatingSystem.IsLinux()) return "linux";
            if (OperatingSystem.IsFreeBSD()) return "freebsd";
            return "unknown";
        }


        public override void Validate() { }

    }
}
