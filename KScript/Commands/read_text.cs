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
            Uri url = null;
            return Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out url) ? new WebClient().DownloadString(path) : File.ReadAllText(path);
        }

        public override void Validate() { }
    }
}
