using KScript.Metadata;
using System.Linq;

namespace KScript.Commands
{
    [KScriptDoc("The average of the numbers.", Returns = "number")]
    public class average : KScriptCommand
    {
        private readonly string[] args;

        public average(params string[] args) => this.args = args;

        public override string Calculate()
        {
            double x = 0.00;
            args.ToList().ForEach(i => x += double.Parse(i, System.Globalization.CultureInfo.InvariantCulture));
            return (x / args.Count()).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public override void Validate() { }
    }
}
