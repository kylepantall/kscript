﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using KScript.CommandHandler;
using KScript.KScriptObjects;

namespace KScript.Handlers
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptCommandHandler
    {
        public const string COMMANDS_NAMESPACE = "KScript.Commands";

        public const string COMMANDS_WITH_PARAMS = @"\@(\w+)\((.+)\)";
        public const string COMMANDS_NO_PARAMS = @"\@(\w+)\(\)";

        public static bool IsCommand(string str, KScriptContainer container, KScriptBaseObject parent)
        {
            Regex cmd_params = new Regex(COMMANDS_WITH_PARAMS);
            Regex cmd_no_params = new Regex(COMMANDS_NO_PARAMS);

            var cmd_no_params_matches = cmd_no_params.Matches(str);
            var cmd_params_matches = cmd_params.Matches(str);

            return cmd_params.IsMatch(str) || cmd_no_params.IsMatch(str);
        }

        public static List<ICommand> GetCommands(string str, KScriptContainer container, KScriptBaseObject baseObj)
        {
            //Count all open brackets, when finding close bracket, length of string to array

            Stack<ICommand> commands = new Stack<ICommand>();

            List<ICommand> All_Commands = new List<ICommand>();

            char[] str_cmds = str.ToCharArray();

            ICommandObject bracket = new ICommandObject(str, container, baseObj);

            ParamTracker paramTracker = new ParamTracker();

            int index = -1;

            bool ignore = false, encountered_cmd = false;

            for (int i = 0; i < str_cmds.Length; i++)
            {
                if (str_cmds[i].Equals('@') && !ignore)
                {
                    encountered_cmd = true;
                }

                if (encountered_cmd)
                {
                    paramTracker.Track(str_cmds[i], i);
                }


                if (str_cmds[i].Equals(char.Parse("'")) && encountered_cmd)
                {
                    ignore = !ignore;
                }

                if (str_cmds[i].Equals('@') && !ignore && encountered_cmd)
                {
                    bracket = new ICommandObject(str, container, baseObj);
                    bracket.IndexProperties.Start = i;
                    bracket.Index = ++index;
                    commands.Push(bracket);
                    continue;
                }
                else if (str_cmds[i].Equals('$') && !ignore && encountered_cmd)
                {
                    ignore = !ignore;
                }
                else if (str_cmds[i].Equals('(') && !ignore && encountered_cmd)
                {
                    if (commands.Peek().IsCommandObject)
                    {
                        commands.Peek().GetCommandObject().EndNameIndex = i;
                    }
                    continue;
                }
                else if (str_cmds[i].Equals(',') && !ignore && encountered_cmd && commands.Count > 0)
                {
                    if (commands.Any())
                    {
                        var cmd = commands.Peek();

                        if (cmd.IsCommandObject)
                        {
                            if (paramTracker.HasParams)
                            {
                                var variable = new IValue(paramTracker.GetIndexPair(), container);
                                cmd.GetCommandObject().Children.Enqueue(variable);
                            }
                        }
                    }
                }
                else if (str_cmds[i].Equals(')') && !ignore && encountered_cmd && commands.Count > 0)
                {
                    if (commands.Any())
                    {
                        var cmd = commands.Pop();
                        cmd.IndexProperties.End = i;

                        if (paramTracker.HasParams)
                        {
                            var variable = new IValue(paramTracker.GetIndexPair(), container);
                            cmd.GetCommandObject().Children.Enqueue(variable);
                        }

                        if (commands.Count > 0)
                        {
                            if (commands.Peek().IsCommandObject && !commands.Peek().GetCommandObject().IsClosed)
                            {
                                commands.Peek().GetCommandObject().Children.Enqueue(cmd);
                            }
                            else
                            {
                                All_Commands.Add(cmd);
                            }
                        }
                        else
                        {
                            All_Commands.Add(cmd);
                        }
                    }
                    else
                    {
                        All_Commands.Add(bracket);
                    }
                    continue;
                }
            }

            return All_Commands;
        }

        public static string HandleCommands(string str, KScriptContainer container, KScriptBaseObject parent, bool parse_immediately = false)
        {
            string temp_string = str;

            var commands = GetCommands(str, container, parent);
            if (commands.Count > 0)
            {
                foreach (var item in commands)
                {
                    if (item.IsCommandObject)
                    {
                        if (parse_immediately)
                        {
                            temp_string = ReplaceFirst(temp_string, item.GetCommandObject().CommandParameters, item.GetCommandObject().CalculateValue());
                        }
                        else
                        {
                            string id = string.Format("[{0}]", Guid.NewGuid().ToString());
                            temp_string = ReplaceFirst(temp_string, item.GetCommandObject().CommandParameters, id);
                            container.GetCommandStore().Add(id, item.GetCommandObject());
                        }
                    }
                }

                if (!parse_immediately)
                {
                    return ReturnCommandString(temp_string, container, parent);
                }
            }

            return temp_string;
        }


        public static string ReturnCommandString(string str, KScriptContainer container, KScriptBaseObject parent)
        {
            if (container.GetCommandStore().Any())
            {
                string temp_string = str;
                container.GetCommandStore().ToList().ForEach(item => temp_string = temp_string.Replace(item.Key, item.Value.GetCommandObject().CalculateValue()));
                container.GetCommandStore().Clear();
                return temp_string;
            }

            return string.Empty;
        }

        public static string ReplaceFirst(string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
            {
                return text;
            }
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        public static KScriptCommand GetCommandObject(string[] @params, Type _type, KScriptContainer container, KScriptBaseObject parent)
        {
            KScriptCommand obj = (KScriptCommand)Activator.CreateInstance(_type, @params);
            obj.Init(container, parent);
            return obj;
        }

        public static KScriptCommand GetCommandObject(Type _type, KScriptContainer container, KScriptBaseObject parent)
        {
            if (_type != null)
            {
                KScriptCommand obj = (KScriptCommand)Activator.CreateInstance(_type);
                obj.Init(container, parent);
                return obj;
            }
            else
            {
                return null;
            }
        }

        public static Type GetCommandType(string type_name) => Assembly.GetExecutingAssembly().GetType(string.Format("{0}.{1}", COMMANDS_NAMESPACE, type_name));
    }
}
