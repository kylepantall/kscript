namespace KScript.VariableFunctions
{
    internal class lte : ITiedVariableFunction
    {

        public lte(KScriptContainer container, string first_variable_id, string second_variable_id)
            : base(container, first_variable_id, second_variable_id) { }

        private int num_one, num_two;

        public override string Evaluate(params string[] args) => ToBoolString(num_one <= num_two);

        public override bool IsAccepted() => int.TryParse(GetFirstDef().Contents, out num_one) && int.TryParse(GetSecondDef().Contents, out num_two);

    }
}
