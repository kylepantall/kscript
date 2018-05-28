using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Commands
{
    public class read_text : KScriptCommand
    {
        private string path = "";

        public read_text(string path) => this.path = path;

        public override string Calculate()
        {
            bool is_url = false;
            try { Uri url = new Uri(path); is_url = url != null; } catch (Exception) { is_url = false; }
            if (is_url) { return new WebClient().DownloadString(path); }
            else return File.ReadAllText(path);
        }
    }
}
