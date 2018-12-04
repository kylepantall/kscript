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
        [KScriptObjects.KScriptExample("$='some example text':'value to update'")]
        [KScriptObjects.KScriptExample("$=1:exit")]
        public string expression { get; set; }


        public override bool Run()
        {
            string ifMatchFunction = @"(\$=)(.+\:.+)";
            string ifEqualsThenAdd = @"(\$\+=)(.+\:.+)";
            string ifThenRandomValue = @"(\$\?\?=)(.+|.+\,)";

            //$#=[number of repetitions,value]
            string repeatFunc = @"\[([0-9]+)\,([^]]+)\]+";

            Regex reg = new Regex(ifMatchFunction);

            string exp = HandleCommands(expression);

            if (Regex.IsMatch(exp, ifMatchFunction))
            {
                Match match = Regex.Match(exp, ifMatchFunction);
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
            else if (Regex.IsMatch(exp, ifEqualsThenAdd))
            {
                Match match = Regex.Match(exp, ifEqualsThenAdd);
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
            else if (Regex.IsMatch(exp, ifThenRandomValue))
            {
                Match match = Regex.Match(exp, ifThenRandomValue);
                string _expression = match.Groups[2].Value;
                string[] Expression = _expression.Split(',');
                int count = Expression.Count();
                Def(to).Contents = Expression[rnd.Next(0, count)];
            }
            else if (exp.StartsWith("$#=") && Regex.IsMatch(exp, repeatFunc))
            {
                //Group 1 is repetition, group 2 is value to repeat

                StringBuilder builder = new StringBuilder();
                MatchCollection matches = Regex.Matches(exp, repeatFunc);
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
