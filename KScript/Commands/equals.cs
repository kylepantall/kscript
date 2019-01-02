namespace KScript.Commands
{
    /// <summary>
    /// Compares values 'a' and 'b' to see if values are equal, returns "yes" if equal, else returns "no".
    /// </summary>
    public class equals : KScriptCommand
    {
        private string a = "", b = "";
        private readonly bool ignore_case = false;

        /// <summary>
        /// Creates a new compare_to KScriptCommand object with the values A and B to compare with case option.
        /// </summary>
        /// <param name="a">Value A to compare against</param>
        /// <param name="b">Value B to compare against</param>
        /// <param name="ignore_case">Whether or not the case of A and B should be ignored</param>
        public equals(string a, string b, string ignore_case = "false") : this(a, b)
        {
            this.ignore_case = ToBool(ignore_case);
        }

        /// <summary>
        /// Creates a new compare_to KScriptCommand object with values A and B to compare.
        /// </summary>
        /// <param name="a">Value A to compare against</param>
        /// <param name="b">Value B to compare against</param>
        public equals(string a, string b)
        {
            this.a = a;
            this.b = b;
        }


        /// <summary>
        /// If ignore case, then compare A and B, else compare (case sensitive).
        /// </summary>
        /// <returns></returns>
        public override string Calculate()
        {
            if (!ignore_case)
            {
                return ToBoolString(a == b);
            }
            else
            {
                return ToBoolString(a.ToLower() == b.ToLower());
            }
        }


        public override void Validate() { }
    }
}
