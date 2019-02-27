using System;

namespace KScript.Arguments.Conditional
{
    class forxy : KScriptObject
    {
        public string x_to { get; set; }
        public string y_to { get; set; }

        public string x_math { get; set; }
        public string y_math { get; set; }

        public string x_while { get; set; }
        public string y_while { get; set; }



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
            throw new NotImplementedException();
        }

        public override void Validate()
        {
            throw new NotImplementedException();
        }
    }
}
