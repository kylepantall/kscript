using KScript.Metadata;
namespace KScript.Commands
{
    [KScriptDoc("yes if a number is between min and max (inclusive).", Returns = "bool")]
    public class range : KScriptCommand
    {
        private readonly string check, max, min = "";

        public range(string check, string min, string max)
        {
            this.check = check;
            this.max = max;
            this.min = min;
        }
        public override string Calculate()
        {
            int max = -1;
            int min = -1;
            int check = -1;


            if (int.TryParse(Format(this.check), out check) &&
                int.TryParse(Format(this.max), out max) &&
                int.TryParse(Format(this.min), out min))
            {
                return ToBoolString(check >= min && check <= max);
            }
            else
            {
                return ToBoolString(false);
            }

        }

        public override void Validate()
        {
            //To implement
        }
    }
}
