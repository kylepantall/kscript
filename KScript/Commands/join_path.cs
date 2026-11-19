using System.IO;
using System.Linq;

namespace KScript.Commands
{
    /// <summary>
    /// Joins path segments using the separator of the current operating system,
    /// e.g. <c>@join_path($dir, 'file.txt')</c> gives <c>dir\file.txt</c> on Windows and <c>dir/file.txt</c> elsewhere.
    /// </summary>
    public class join_path : KScriptCommand
    {
        private readonly string[] parts;

        public join_path(params string[] parts) => this.parts = parts;

        public override string Calculate() => Path.Combine(parts.Where(p => !string.IsNullOrEmpty(p)).ToArray());

        public override void Validate() { }
    }
}
