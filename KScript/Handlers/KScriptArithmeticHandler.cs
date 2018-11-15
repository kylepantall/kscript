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
        public static string HandleCalculation(string str)
        {
            Expression e = new Expression(str);
            double v = e.calculate();
            return v.ToString();
        }

        public static string HandleAnds(string str)
        {
            throw new NotImplementedException();
            //List<string> andStatements = str.Split(new string[] { "&&" }, StringSplitOptions.None).ToList();
            //return null;
        }
    }
}
