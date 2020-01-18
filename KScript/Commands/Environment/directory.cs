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
            startmenu = 11
        }

        public directory(string val) : this()
        {
            directory_values directory_value = directory_values.desktop;
            Enum.TryParse(val, out directory_value);
            _directory = System.Environment.GetFolderPath((Environment.SpecialFolder)(int)directory_value);
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
