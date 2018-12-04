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
        /**
         * Calls are used to identify any block of script defined within a method container
         */
        public const string CALLS = "calls";
        public const string EXCEPTIONS = "exceptions";

        public const string VARIABLE_NO_POINTERS = @"\$\b\S+\b(?!\-\>)";
        public const string VARIABLE_POINTERS = @"\$(\w+)(?:\-\>)(\w+)\(\)";


        //public const string SPLIT_ARRAY_EXPRESSION = @"(?<!,[^(]+\([^)]+),";
        public const string ARRAY_CHECK_EXPRESSION = @"("".*?""|[^"",\s]+)(?=\s*,|\s*$)";
    }
}
