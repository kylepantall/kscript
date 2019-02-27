namespace KScript.Handlers
{
    class KScriptArrayHandler
    {
        /// <summary>
        /// Used to extract the multidimensional array
        /// 
        /// Group 1: Array Name followed by Key pointers or array indexes: myarray[1][2] etc.
        /// Group 2: Array Name
        /// Group 3: Final Key Pointer or Index e.g. [1][2][3] would return 3
        /// </summary>
        public const string ArrayFinderRegex = @"\~((.+)(\[[^[\]]+\]))";

        /// <summary>
        /// Used to extract the parameters within the square brackets.
        /// 
        /// Group 1: Parameter with square brackets: [1] => [1]
        /// Group 2: Parameter within square brackets: [1] => 1
        /// </summary>
        public const string ArrayIndexKeyRegex = @"(\[([^\[\]]+)\]+)";
    }
}
