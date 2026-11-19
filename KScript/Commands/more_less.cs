using KScript.KScriptExceptions;

namespace KScript.Commands
{
    public class more_less : KScriptCommand
    {
        private readonly string firstValue = "";
        private string secondValue = "";
        private string lastValue = "<";

        public readonly string[] Operators = { "mt", "mte", "lt", "lte" };
        public more_less(string firstNumber, string secondNumber) { firstValue = firstNumber; secondValue = secondNumber; }
        public more_less(string firstComparison, string middleValue, string finalValue) : this(firstComparison, middleValue) {
            lastValue = finalValue;
        }

        public override string Calculate()
        {
            bool isSecondValueOperator = IsWithinHaystack(secondValue, Operators) && !IsNumber(secondValue);

            if (isSecondValueOperator)
            {
                (secondValue, lastValue) = (lastValue, secondValue);
            }

            string firstNumber = KScript().GetStringHandler().Format(firstValue),
                secondNumber = KScript().GetStringHandler().Format(secondValue);

            switch ((lastValue ?? "lt").Trim().ToLowerInvariant())
            {
                case "mt":
                    return ToBoolString(ParseNumber(firstNumber) > ParseNumber(secondNumber));
                case "mte":
                    return ToBoolString(ParseNumber(firstNumber) >= ParseNumber(secondNumber));
                case "lt":
                    return ToBoolString(ParseNumber(firstNumber) < ParseNumber(secondNumber));
                case "lte":
                    return ToBoolString(ParseNumber(firstNumber) <= ParseNumber(secondNumber));
                default:
                    return ToBoolString(ParseNumber(firstNumber) < ParseNumber(secondNumber));
            }
        }

        private double ParseNumber(string value)
        {
            if (!double.TryParse(value?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                throw new KScriptExceptions.KScriptException(this, $"@more_less expects numbers but got '{value}'.");
            }
            return result;
        }

        public override void Validate()
        {
            if (string.IsNullOrEmpty(firstValue) || string.IsNullOrEmpty(secondValue) || string.IsNullOrEmpty(lastValue))
            {
                throw new KScriptValidationFail(this, "Value cannot be NULL");
            }
        }
    }
}
