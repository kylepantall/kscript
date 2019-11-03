namespace KScript.Global
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
    }
}
