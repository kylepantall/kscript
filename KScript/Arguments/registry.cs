using KScript.KScriptTypes.KScriptExceptions;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments
{
    public class registry : KScriptObject
    {
        public string subkey { get; set; }
        public string key { get; set; }
        public string contents { get; set; }
        private RegistryValueKind _type = RegistryValueKind.QWord;

        public registry(string contents) => this.contents = contents;

        public string @type
        {
            get { return Enum.GetName(typeof(RegistryValueKind), _type); }
            set { try { _type = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), value, true); } catch (Exception) { _type = RegistryValueKind.String; } }
        }

        public override bool Run()
        {
            Registry.SetValue(key, subkey, contents, _type);
            return true;
        }

        public string GetTypes()
        {
            StringBuilder build = new StringBuilder();
            Enum.GetNames(typeof(RegistryValueKind)).ToList().ForEach(i => build.AppendLine(string.Format("- {0}", i)));
            return build.ToString();
        }

        public override string UsageInformation() => @"Used to modify regsitry values.\nProperties required: - subkey\n- key\n- inner text"
            + @"\n\n" + GetTypes();

        public override void Validate()
        {
            if (string.IsNullOrWhiteSpace(subkey) || string.IsNullOrWhiteSpace(subkey) || string.IsNullOrWhiteSpace(contents))
                throw new KScriptInvalidScriptType();
            else throw new KScriptNoValidationNeeded();
        }
    }
}
