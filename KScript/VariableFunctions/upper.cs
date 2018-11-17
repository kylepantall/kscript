namespace KScript.VariableFunctions
{
    class upper : IVariableFunction
    {
        public string Evaluate(string value) => value.ToUpper();
    }
}
