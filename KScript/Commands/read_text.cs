using System;
using System.IO;
using System.Net;

namespace KScript.Commands
{
    public class read_text : KScriptCommand
    {
        private readonly string path = "";

        public read_text(string path) => this.path = path;

        public override string Calculate()
        {
            bool is_url = false;
            string Result = string.Empty;
            try { Uri url = new Uri(path); is_url = url != null; } catch (Exception ex) { is_url = false; HandleException(ex, this); }

            if (is_url) { Result = new WebClient().DownloadString(path); }
            else
            {
                Result = File.ReadAllText(path);
            }

            return Result;
        }

        public override void Validate() { }
    }
}
