using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KScript.Handlers
{
    public class KScriptCommandHandler
    {
        public static string HandleCommands(string str, KScriptContainer container)
        {
            string commands_with_params = @"\@\w+\(.+\)";
            string commands_no_params = @"\@\w+\(\)";


            Regex cmd_params = new Regex(commands_with_params);
            Regex cmd_no_params = new Regex(commands_no_params);


            foreach (Match item in cmd_no_params.Matches(str))
            {
                String cmd_type = item.Value.Substring(1, item.Value.Length - 2);
                Console.Out.Write(cmd_type);
                //Type _type = Assembly.GetExecutingAssembly().GetType(string.Format("KScript.Arguments.{0}", item.Value));
            }

            return "";
        }
    }
}
