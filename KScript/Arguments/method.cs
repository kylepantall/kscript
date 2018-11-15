namespace KScript.Arguments
{
    public class method : KScriptMethodWrapper
    {
        public override bool Run()
        {
            string[] _params = @params.Split(',');
            foreach (var item in _params)
            {
                string variable_name = string.Format("{0}.{1}", name, item);
                ParentContainer.defs.Add(variable_name, new def(""));
            }
            return true;
        }
    }
}
