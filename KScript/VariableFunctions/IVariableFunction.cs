namespace KScript.VariableFunctions
{
    public interface IVariableFunction
    {
        /// <summary>
        /// Evaluate the result based on the input of the given variable [value].
        /// </summary>
        /// <param name="value">The value of the variable</param>
        /// <returns>Evaluated string</returns>
        string Evaluate(string value);

        /// <summary>
        /// Evaluates if this variable function can be executed on this type of value.
        /// </summary>
        /// <returns>If this type of variable is accepted.</returns>
        bool IsAccepted(string value);
    }
}
