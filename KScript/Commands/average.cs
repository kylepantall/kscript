using System.Linq;

namespace KScript.Commands
{
    public class average : KScriptCommand
    {
        private readonly string[] args;

        public average(params string[] args) => this.args = args;

        public override string Calculate()
        {
            double x = 0.00;
            args.ToList().ForEach(i => x += double.Parse(i));
            return (x / args.Count()).ToString();
        }

        public override void Validate() { }
    }
}
