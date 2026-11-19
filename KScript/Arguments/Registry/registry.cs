using KScript.Metadata;
#pragma warning disable CA1416 // Run() checks OperatingSystem.IsWindows() before touching the registry.
using KScript.KScriptExceptions;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Text;

namespace KScript.Arguments
{
    [KScriptDoc("Reads or writes the Windows registry.")]
    [KScriptElement(KScriptContentKind.Template, Context = KScriptElementContext.Script)]
    public class registry : KScriptObject
    {
        public registry(string Contents) => this.Contents = Contents;

        private RegistryValueKind _type = RegistryValueKind.QWord;

        [KScriptObjects.KScriptProperty("The subkey to use when finding/creating the registry key.", true)]
        public string subkey { get; set; }

        [KScriptObjects.KScriptProperty("The key to use when finding/creating the registry key.", true)]
        public string key { get; set; }


        [KScriptObjects.KScriptProperty("The registry type - by default is QWord.", true)]
        [KScriptObjects.KScriptAcceptedOptions("Binary", "None", "Unknown", "String", "ExpandString", "DWord", "MultiString", "QWord")]
        public string @type
        {
            get { return Enum.GetName(typeof(RegistryValueKind), _type); }
            set { try { _type = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), value, true); } catch (Exception) { _type = RegistryValueKind.String; } }
        }

        public override bool Run()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new KScriptException(this, "<registry> is only supported on Windows.");
            }

            Registry.SetValue(key, subkey, Contents, _type);
            return true;
        }

        public string GetTypes()
        {
            StringBuilder build = new StringBuilder();
            Enum.GetNames(typeof(RegistryValueKind)).ToList().ForEach(i => build.AppendLine(string.Format("\t\t\t- {0}", i)));
            return build.ToString();
        }

        public override string UsageInformation() => "Used to modify regsitry values.\n";

        public override void Validate()
        {
            if (string.IsNullOrWhiteSpace(subkey) || string.IsNullOrWhiteSpace(subkey) || string.IsNullOrWhiteSpace(Contents))
            {
                throw new KScriptInvalidScriptType(this);
            }
            else
            {
                throw new KScriptNoValidationNeeded(this);
            }
        }
    }
}
