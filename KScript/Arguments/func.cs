using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KScript.Arguments
{
    public class func : KScriptObject
    {
        private Random rnd;

        public func(string contents)
        {
            Contents = contents;
            rnd = new Random();
        }

        [KScriptObjects.KScriptProperty("Used to identify the def to update.", true)]
        [KScriptObjects.KScriptAcceptedOptions("$[id]")]
        public string to { get; set; }


        [KScriptObjects.KScriptProperty("Used to declare a expression.", true)]
        [KScriptObjects.KScriptExample("$='some value to replace':'value to update with'")]
        [KScriptObjects.KScriptExample("$=1:settings,2:exit")]
        public string expression { get; set; }


        public override bool Run()
        {
            Regex reg = new Regex(Global.GlobalIdentifiers.IFMATCHFUNCTION);

            string exp = HandleCommands(expression);

            if (Regex.IsMatch(exp, Global.GlobalIdentifiers.IFMATCHFUNCTION))
            {
                Match match = Regex.Match(exp, Global.GlobalIdentifiers.IFMATCHFUNCTION);
                string _expression = match.Groups[2].Value;
                string[] Expression = _expression.Split(',');

                foreach (var item in Expression)
                {
                    string find = item.Split(':')[0];
                    string replace = item.Split(':')[1];
                    if (Def(to).Contents == find)
                    {
                        Def(to).Contents = replace;
                    }
                }
            }
            else if (Regex.IsMatch(exp, Global.GlobalIdentifiers.IFEQUALSTHENADD))
            {
                Match match = Regex.Match(exp, Global.GlobalIdentifiers.IFEQUALSTHENADD);
                string _expression = match.Groups[2].Value;
                string[] Expression = _expression.Split(',');

                foreach (var item in Expression)
                {
                    string find = item.Split(':')[0];
                    string addition = item.Split(':')[1];

                    bool isNumber = true, isDefNumber = true;

                    try { int val = int.Parse(addition); }
                    catch (Exception) { isNumber = false; }
                    try { int val = int.Parse(Def(to).Contents); }
                    catch (Exception) { isDefNumber = false; }


                    if (Def(to).Contents == find)
                    {
                        if (isDefNumber && isNumber)
                        {
                            Def(to).Contents = (int.Parse(Def(to).Contents) + int.Parse(addition)).ToString();
                        }
                        else { Def(to).Contents = (Def(to).Contents += addition); }
                    }
                }
            }
            else if (Regex.IsMatch(exp, Global.GlobalIdentifiers.IFTHENRANDOMVALUE))
            {
                Match match = Regex.Match(exp, Global.GlobalIdentifiers.IFTHENRANDOMVALUE);
                string _expression = match.Groups[2].Value;
                string[] Expression = _expression.Split(',');
                int count = Expression.Count();
                Def(to).Contents = Expression[rnd.Next(0, count)];
            }
            else if (exp.StartsWith("$#=") && Regex.IsMatch(exp, Global.GlobalIdentifiers.REPEATFUNCTION))
            {
                //Group 1 is repetition, group 2 is value to repeat

                StringBuilder builder = new StringBuilder();
                MatchCollection matches = Regex.Matches(exp, Global.GlobalIdentifiers.REPEATFUNCTION);
                foreach (Match match in matches.Cast<Match>())
                {
                    int repeat = int.Parse(match.Groups[1].Value);
                    string value = HandleCommands(match.Groups[2].Value);
                    for (int i = 0; i < repeat; i++)
                    {
                        builder.Append(value);
                    }
                }
                Def(to).Contents = builder.ToString();
            }
            return true;
        }

        public override string UsageInformation() => "Used to update a value based on its value";

        public override void Validate() => new KScriptExceptions.KScriptNoValidationNeeded(this);
    }
}
