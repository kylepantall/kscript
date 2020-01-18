using org.mariuszgromada.math.mxparser;

namespace KScript.Handlers
{
    /// <summary>
    /// Handles the computation of mathematical conditions
    /// </summary>
    public static class KScriptArithmeticHandler
    {
        public static string HandleCalculation(string str) => new Expression(str).calculate().ToString();
    }
}
