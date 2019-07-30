using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Arguments.Threading
{
    class thread : KScriptMethodWrapper
    {
        public override bool Run()
        {
            if (!string.IsNullOrEmpty(@params))
            {
                string[] _params = @params.Split(',');
                foreach (var item in _params)
                {
                    string variable_name = string.Format("{0}.{1}", name, item);
                    ParentContainer.AddDef(variable_name, new def(""));
                }
            }
            return true;
        }
    }
}
