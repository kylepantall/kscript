using System.IO;

namespace KScript.Commands
{
    class dir_exists : KScriptCommand
    {
        private readonly string dir = "";
        public dir_exists(string dir) => this.dir = dir;

        public override string Calculate() => ToBoolString(Directory.Exists(dir));
        public override void Validate() => throw new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}
