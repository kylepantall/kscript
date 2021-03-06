﻿namespace KScript.Global
{
    static class Time
    {
        public const int Minute = 60000;
        public const int Second = 1000;
        public const int Millisecond = 1;
    }

    static class Values
    {
        public const string NULL = "NULL";
    }

    static class Booleans
    {
        public const string YES = "yes";
        public const string NO = "no";
        public static readonly string[] YES_VALUES = { "yes", "y", "t", "true", "1" };
        public static readonly string[] NO_VALUES = { "no", "n", "f", "false", "0" };
    }

    static class GlobalIdentifiers
    {
        // Calls are used to identify any block of script defined within a method container
        public const string CALLS = "calls";
        public const string EXCEPTIONS = "exceptions";

        // Exception references
        public const string EXCEPTION_TYPES = "KScript.KScriptExceptions";

        // $variable Expressions
        public const string VARIABLE_NO_POINTERS = @"\$\b\S+\b(?!\-\>)";
        public const string VARIABLE_NAME_DETECTION = @"^\w+$";
        public const string VARIABLE_POINTERS = @"\$([A-Za-z0-9-_.]+)(?:\-\>)([A-Za-z0-9-_.]+)\(\)";
        public const string VARIABLE_TIED_POINTERS = @"\$([A-Za-z0-9-_.]+)\%\$([A-Za-z0-9-_.]+)(?:\-\>)([A-Za-z0-9-_.]+)\(\)";
        public const string VARIABLE_POINTERS_CORRECTION = @"\%\$([A-Za-z0-9-_.]+)(?:\-\>)([A-Za-z0-9-_.]+)\(\)";

        //<func/> Expressions
        public const string IFMATCHFUNCTION = @"(\$=)(.+\:.+)";
        public const string IFEQUALSTHENADD = @"(\$\+=)(.+\:.+)";
        public const string IFTHENRANDOMVALUE = @"(\$\?\?=)(.+|.+\,)";
        public const string REPEATFUNCTION = @"\[([0-9]+)\,([^]]+)\]+";

        //Commands
        public const string COMMANDS_NAMESPACE = "KScript.Commands";
        public const string VARIABLE_FUNCTIONS_NAMESPACE = "KScript.VariableFunctions";

        public const string COMMANDS_WITH_PARAMS = @"\@(\w+)\((.+)\)";
        public const string COMMANDS_NO_PARAMS = @"\@(\w+)\(\)";

        //Array
        public const string ARRAY_CHECK_EXPRESSION = @"("".*?""|[^"",\s]+)(?=\s*,|\s*$)";


        //Paragraph Expressions
        public const string PARAGRAPH_EXPRESSION = @"\""(.+)\""";


        //Constant to store Assembly path for KScript Arguments.
        public const string ASSEMBLY_PATH = "KScript.Arguments";
        public const string VARIABLE_ASSEMBLY_PATH = "KScript.VariableFunctions";

        /// <summary>
        /// Constant which determines if KScript should return all exceptions instead of attempting to handle them.
        /// </summary>

        public const string PARSER_HANDLERS = "KScript.KScriptParserHandlers";


        public const string OPERATOR_HANDLERS = "KScript.KScriptOperatorHandlers";
    }
}
