using System;

namespace KScript.Commands
{
    class directory : KScriptCommand
    {
        private readonly string _directory = string.Empty;

        public directory()
        {
            _directory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
        }


        private enum directory_values
        {
            desktop = 16,
            documents = 5,
            pictures = 39,
            music = 13,
            videos = 14,
            appdata = 26,
            startmenu = 11,
            home = 40
        }

        public directory(string val) : this()
        {
            directory_values directory_value = directory_values.desktop;
            string name = val?.Trim().ToLowerInvariant();

            if (name == "temp")
            {
                _directory = System.IO.Path.GetTempPath();
                return;
            }

            if (name == "current")
            {
                _directory = System.IO.Directory.GetCurrentDirectory();
                return;
            }

            Enum.TryParse(name, true, out directory_value);
            _directory = System.Environment.GetFolderPath((Environment.SpecialFolder)(int)directory_value);

            // Some folders (e.g. Desktop on a headless Linux box) do not exist on every platform.
            if (string.IsNullOrEmpty(_directory))
            {
                _directory = System.Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }


        public override string Calculate()
        {
            return _directory;
        }

        public override void Validate()
        {
            throw new KScriptExceptions.KScriptNoValidationNeeded(this);
        }
    }
}
