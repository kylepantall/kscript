using KScript.Metadata;
namespace KScript.Commands
{
    [KScriptRetired("Use the not operator: not $a.")]
    public class not : KScriptCommand
    {
        public not(string value) => Value = value;
        private string Value { get; set; }
        public override string Calculate() => ToBoolString(!ToBool(Value));

        public override void Validate() { }
    }
}
