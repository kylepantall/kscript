using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using KScript.VariableFunctions;

namespace KScript
{
    [ClassInterface(ClassInterfaceType.None)]
    public static class KScriptVariableHandler
    {
        public const string TRALING_STRING = "   ";

        public static bool IsVariable(string val) => Regex.IsMatch(val, Global.GlobalIdentifiers.VARIABLE_NO_POINTERS) || Regex.IsMatch(val, Global.GlobalIdentifiers.VARIABLE_POINTERS);
        public static bool IsVariablePointer(string val) => Regex.IsMatch(val, Global.GlobalIdentifiers.VARIABLE_POINTERS);

        public static string ReturnFormattedVariables(KScriptContainer ParentContainer, string Contents)
        {
            string temp_string = string.Format("{0}{1}", TRALING_STRING, Contents);
            ParentContainer.GetDefs().ToList()
                .ForEach(
                    item => temp_string = CalculateValue(ParentContainer, temp_string, item.Key, item.Value.Contents));
            return temp_string.Substring(TRALING_STRING.Length);
        }

        public static string CalculateValue(KScriptContainer ParentContainer, string contents, string key, string value)
        {
            string result = contents;

            MatchCollection pointer_matches = Regex.Matches(contents, Global.GlobalIdentifiers.VARIABLE_POINTERS, RegexOptions.Multiline);
            MatchCollection tied_pointer_matches = Regex.Matches(contents, Global.GlobalIdentifiers.VARIABLE_TIED_POINTERS, RegexOptions.Multiline);

            if (tied_pointer_matches.Count > 0)
            {
                foreach (Match match in tied_pointer_matches)
                {
                    string variable_id = match.Groups[1].Value;
                    string variable_second_id = match.Groups[2].Value;
                    string variable_func = match.Groups[3].Value;

                    ITiedVariableFunction func = ParentContainer.Parser.GetTiedVariableFunction(variable_id, variable_second_id, variable_func, ParentContainer);
                    if (func != null)
                    {
                        if (func.IsAccepted())
                        {
                            string val = func.Evaluate(null);
                            string replace = string.Format(@"\$(\b{0}\b)\%\$(\b{1}\b)(?:\-\>)(\b{2}\b)\(\)", variable_id, variable_second_id, variable_func);
                            result = Regex.Replace(contents, replace, val);
                        }
                    }
                }
            }

            if (pointer_matches.Count > 0 && !Regex.IsMatch(contents, Global.GlobalIdentifiers.VARIABLE_POINTERS_CORRECTION))
            {
                foreach (Match match in pointer_matches)
                {
                    string variable_id = match.Groups[1].Value;
                    string variable_func = match.Groups[2].Value;
                    IVariableFunction func = ParentContainer.Parser.GetVariableFunction(variable_id, variable_func, ParentContainer);
                    if (func != null)
                    {
                        if (func.IsAccepted())
                        {
                            string val = func.Evaluate(null);
                            string replace = string.Format(@"\$\b{0}\b\-\>\b{1}\b\(\)", variable_id, variable_func);
                            result = Regex.Replace(contents, replace, val);
                        }
                    }
                }
            }


            result = Regex.Replace(result, string.Format(@"\$\b{0}\b(?!\-\>)", key), value, RegexOptions.IgnoreCase);

            return result;
        }
    }
}
