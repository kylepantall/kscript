namespace KScript.Commands
{
    class to_base64 : KScript.KScriptCommand
    {
        public string input { get; set; }
        public to_base64(string value) => input = value;

        private string convertTo(string value)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(value);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public override string Calculate()
        {
            return convertTo(this.input);
        }

        public override void Validate()
        {
            throw new KScriptExceptions.KScriptNoValidationNeeded(this);
        }
    }
}