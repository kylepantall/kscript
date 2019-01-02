namespace KScript.Commands
{
    class ifnull : KScriptCommand
    {
        private readonly string Value;
        private readonly string Else;

        public ifnull(string value, string @else)
        {
            Value = value;
            Else = @else;
        }

        public override string Calculate() => (Value == Global.Values.NULL || string.IsNullOrWhiteSpace(Value) ? Else : Value);

        public override void Validate() { }
    }
}
