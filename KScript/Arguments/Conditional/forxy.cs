namespace KScript.Arguments.Conditional
{
    class forxy : KScriptObject
    {
        public string to_x { get; set; }
        public string to_y { get; set; }

        public string x_math { get; set; }
        public string y_math { get; set; }

        public string @while { get; set; }

        public forxy()
        {
            if (!string.IsNullOrWhiteSpace(x_math))
            {
                x_math = $"${to_x}->increment()";
            }

            if (!string.IsNullOrWhiteSpace(y_math))
            {
                y_math = $"${to_y}->increment()";
            }
        }


        public override bool Run()
        {
            //int x_val = int.Parse(Def(x_to).Contents);
            //int y_val = int.Parse(Def(y_to).Contents);

            //for (int x = x_val; ToBool(HandleCommands(x_while)); x = int.Parse(HandleCommands(x_math)))
            //{
            //    for (int y = x_val; ToBool(HandleCommands(y_while)); y = int.Parse(HandleCommands(y_math)))
            //    {

            //    }
            //}
            return true;
        }

        public override string UsageInformation()
        {
            return "To loop in an iteration such that there exists an X for every Y.";
        }

        public override void Validate()
        {
            //throw new NotImplementedException();
        }
    }
}
