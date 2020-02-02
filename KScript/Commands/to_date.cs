using System.Text.RegularExpressions;
using System;
using KScript.KScriptExceptions;

namespace KScript.Commands
{
    public class to_date : KScriptCommand
    {
        private readonly string dateTime;
        private readonly string calculation;

        private const string DateRegex = @"D(\w+)";
        private const string DateSplitRegex = @"(([DY]|MO)(\d+))";
        private const string TimeRegex = @"T(\w+)";
        private const string TimeSplitRegex = @"(([HMS])(\d+))";

        public to_date(string dateTime) => this.dateTime = dateTime;
        public to_date(string dateTime, string calculation) : this(dateTime) => this.calculation = calculation;

        public override string Calculate()
        {

            DateTime value = DateTime.Parse(dateTime);

            if (IsEmpty(calculation))
            {
                return value.ToString();
            }


            MatchCollection dateCalcs = Regex.Matches(calculation, DateSplitRegex);
            MatchCollection timeCalc = Regex.Matches(calculation, TimeSplitRegex);

            foreach (Match item in timeCalc)
            {
                string timePick = item.Groups[2].Value;
                int timeVal = int.Parse(item.Groups[3].Value);

                switch (timePick.ToUpper())
                {
                    case "H":
                        value = value.AddHours(timeVal);
                        break;
                    case "M":
                        value = value.AddMinutes(timeVal);
                        break;
                    case "S":
                        value = value.AddSeconds(timeVal);
                        break;
                    default:
                        break;
                }
            }


            foreach (Match item in dateCalcs)
            {
                string datePick = item.Groups[2].Value;
                int dateVal = int.Parse(item.Groups[3].Value);

                switch (datePick.ToUpper())
                {
                    case "D":
                        value = value.AddDays(dateVal);
                        break;
                    case "MO":
                        value = value.AddMonths(dateVal);
                        break;
                    case "Y":
                        value = value.AddYears(dateVal);
                        break;
                    default:
                        break;
                }
            }

            return value.ToString();
        }


        public override void Validate() { }
    }
}
