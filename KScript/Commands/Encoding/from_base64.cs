namespace KScript.Commands
{
    class from_base64 : KScript.KScriptCommand
    {
        public string input { get; set; }
        public from_base64(string value) => input = value;

        private string convertFrom(string value)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(value);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        public override string Calculate()
        {
            return convertFrom(this.input);
        }

        public override void Validate()
        {
            throw new KScriptExceptions.KScriptNoValidationNeeded(this);
        }
    }
}