using org.mariuszgromada.math.mxparser;

namespace KScript.Handlers
{
    /// <summary>
    /// Handles the computation of mathematical conditions
    /// </summary>
    public static class KScriptArithmeticHandler
    {
        public static string HandleCalculation(string str)
        {
            Expression e = new Expression(str);
            double v = e.calculate();
            return v.ToString();
        }
    }
}
