using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace KScript.Handlers
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptCommandHandler
    {
        public const string COMMANDS_NAMESPACE = "KScript.Commands";

        public static string HandleCommands(string str, KScriptContainer container)
        {
            String temp_string = str;
            //string command_with_params_test = @"\@(?<=(?<open>\()).*(?=(?<close>\)))";
            string commands_with_params = @"\@(\w+)\((.+)\)";
            string commands_no_params = @"\@(\w+)\(\)";

            Regex cmd_params = new Regex(commands_with_params);
            Regex cmd_no_params = new Regex(commands_no_params);

            var cmd_no_params_matches = cmd_no_params.Matches(temp_string);
            var cmd_params_matches = cmd_params.Matches(temp_string);

            foreach (Match with_params in cmd_params_matches)
            {
                string[] strs = Regex.Split(HandleCommands(with_params.Groups[2].Value, container), @"(?<!,[^(]+\([^)]+),");

                List<string> new_strs = new List<string>();
                foreach (var item in strs)
                {
                    new_strs.Add(HandleCommands(item.Trim('\"'), container));
                }

                string[] @params = new_strs.ToArray();

                string type_name = with_params.Groups[1].Value;
                Type _type = GetCommandType(type_name);

                KScriptCommand cmd = GetCommandObject(@params, _type, container);
                temp_string = temp_string.Replace(with_params.Value, cmd.Calculate());
            }

            foreach (Match without_params in cmd_no_params_matches)
            {
                string type_name = without_params.Groups[1].Value;
                Type _type = GetCommandType(type_name);
                KScriptCommand cmd = GetCommandObject(_type, container);
                temp_string = temp_string.Replace(without_params.Value, cmd.Calculate());
            }

            return temp_string;
        }

        private static KScriptCommand GetCommandObject(string[] @params, Type _type, KScriptContainer container)
        {
            KScriptCommand obj = (KScriptCommand)Activator.CreateInstance(_type, @params);
            obj.Init(container);
            return obj;
        }

        private static KScriptCommand GetCommandObject(Type _type, KScriptContainer container)
        {
            if (_type != null)
            {
                KScriptCommand obj = (KScriptCommand)Activator.CreateInstance(_type);
                obj.Init(container);
                return obj;
            }
            else
            {
                return null;
            }
        }

        private static Type GetCommandType(string type_name) => Assembly.GetExecutingAssembly().GetType(string.Format("{0}.{1}", COMMANDS_NAMESPACE, type_name));
    }
}
