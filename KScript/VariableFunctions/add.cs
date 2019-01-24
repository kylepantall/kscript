namespace KScript.VariableFunctions
{
    class add : ITiedVariableFunction
    {
        public add(KScriptContainer container, string first_variable_id, string second_variable_id)
            : base(container, first_variable_id, second_variable_id) { }

        private int number1, number2;

        public override string Evaluate(params string[] args) => (number1 + number2).ToString();

        public override bool IsAccepted()
        {
            bool bothNumebrs = int.TryParse(GetFirstDef().Contents, out number1) && int.TryParse(GetSecondDef().Contents, out number2);
            return bothNumebrs;
        }
    }
}
