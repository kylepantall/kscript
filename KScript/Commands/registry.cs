using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class registry : KScriptCommand
    {
        private string path { get; set; }
        private string key { get; set; }

        public registry(string path, string key) { this.path = path; this.key = key; }
        public override string Calculate() => Registry.GetValue(path, key, string.Empty).ToString();
    }
}
