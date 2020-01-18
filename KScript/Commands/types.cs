using System.Linq;
using System.Text;

namespace KScript.Commands
{
    public class types : KScriptCommand
    {
        public types() { }
        public override string Calculate()
        {
            var builder = new StringBuilder();
            System.Reflection.Assembly.GetExecutingAssembly().GetTypes().ToList().ForEach(i =>
            {
                builder.AppendLine(i.FullName);
            });
            return builder.ToString();
        }
        public override void Validate() { }
    }
}
