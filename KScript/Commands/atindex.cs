namespace KScript.Commands
{
    class atindex : KScriptCommand
    {
        private readonly string str;
        private readonly string index;

        public atindex(string str, string index)
        {
            this.str = str;
            this.index = index;
        }

        public override string Calculate()
        {
            int index = -1;
            if (int.TryParse(this.index, out index))
            {
                if (str.Length >= index)
                {
                    return str[index].ToString();
                }
                return string.Empty;
            }

            return string.Empty;
        }

        public override void Validate() { }
    }
}
