using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using org.mariuszgromada.math.mxparser;

namespace KScript.Handlers
{
    /// <summary>
    /// Handles the computation of mathematical conditions
    /// </summary>
    public static class KScriptArithmeticHandler
    {
        public static string HandleAdditions(string str)
        {
            List<string> splits = str.Split('+').Select(i => i.Trim()).ToList();
            int total = 0;
            splits.ForEach(i => total += ParseInt(i, 0));
            return total.ToString();
        }


        public static string HandleCalculation(string str)
        {
            Expression e = new Expression(str);
            double v = e.calculate();
            return v.ToString();
        }


        public static int ParseInt(string value, int defaultValue)
        {
            try { return int.Parse(value); }
            catch (Exception) { return defaultValue; }
        }
    }
}
