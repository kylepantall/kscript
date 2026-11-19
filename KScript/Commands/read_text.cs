using System;
using System.IO;
using System.Net.Http;

namespace KScript.Commands
{
    public class read_text : KScriptCommand
    {
        private readonly string path = "";

        public read_text(string path) => this.path = path;

        public override string Calculate()
        {
            // Previously any path was treated as a URL (Uri.TryCreate with RelativeOrAbsolute accepts almost anything),
            // so local files could never be read.
            if (Helpers.Platform.IsHttpUrl(path))
            {
                using (HttpClient client = new HttpClient())
                {
                    return client.GetStringAsync(path).GetAwaiter().GetResult();
                }
            }

            return File.ReadAllText(Helpers.Platform.ResolvePath(path, KScript()?.GetFileDirectory()));
        }

        public override void Validate() { }
    }
}
