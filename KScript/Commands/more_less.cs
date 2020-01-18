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
        public more_less(string firstComparison, string middleValue, string finalValue) : this(firstComparison, middleValue) => lastValue = finalValue;

        public override string Calculate()
        {
            bool isSecondValueOperator = IsWithinHaystack(secondValue, Operators) && !IsNumber(secondValue);

            if (isSecondValueOperator)
            {
                (secondValue, lastValue) = (lastValue, secondValue);
            }

            string firstNumber = KScript().GetStringHandler().Format(firstValue),
                secondNumber = KScript().GetStringHandler().Format(secondValue);

            switch (lastValue.ToLower())
            {
                case "mt":
                    return ToBoolString(double.Parse(firstNumber) > double.Parse(secondNumber));
                case "mte":
                    return ToBoolString(double.Parse(firstNumber) >= double.Parse(secondNumber));
                case "lt":
                    return ToBoolString(double.Parse(firstNumber) < double.Parse(secondNumber));
                case "lte":
                    return ToBoolString(double.Parse(firstNumber) <= double.Parse(secondNumber));
                default:
                    return ToBoolString(double.Parse(firstNumber) < double.Parse(secondNumber));
            }
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
