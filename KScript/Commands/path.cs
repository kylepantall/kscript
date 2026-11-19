using KScript.Metadata;
using System;

namespace KScript.Commands
{
    [KScriptDoc("A part of a path: filename, directory, extension...")]
    public class path : KScriptCommand
    {
        private readonly path_type type = path_type.filename;
        private readonly string _path;

        public path(string path)
        {
            type = path_type.filename;
            _path = path;
        }

        public path(string path, string type) : this(path)
        {
            int num = -1;
            path_type value = path_type.filename;

            if (int.TryParse(type, out num))
            {
                this.type = (path_type)num;
            }
            else
            {
                if (Enum.TryParse(type?.Trim(), true, out value))
                {
                    this.type = value;
                }
                else
                {
                    throw new KScriptExceptions.KScriptException(this, "Incorrect path type.");
                }
            }
        }

        public enum path_type
        {
            filename = 0,
            directory = 1,
            fullpath = 2,
            extension = 3,
            name = 4
        }


        public override string Calculate()
        {
            string path = _path;

            switch (type)
            {
                case path_type.filename:
                    return System.IO.Path.GetFileName(path);
                case path_type.directory:
                    return System.IO.Path.GetDirectoryName(path);
                case path_type.fullpath:
                    return System.IO.Path.GetFullPath(path);
                case path_type.extension:
                    return System.IO.Path.GetExtension(path);
                case path_type.name:
                    return System.IO.Path.GetFileNameWithoutExtension(path);
                default:
                    return _path;
            }
        }


        public override void Validate() { }
    }
}
