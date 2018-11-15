namespace KScript.Global
{
    static class Time
    {
        public const int Minute = 60000;
        public const int Second = 1000;
        public const int Millisecond = 1;
    }


    static class GlobalIdentifiers
    {
        /**
         * Calls are used to identify any block of script defined within a method container
         */
        public const string CALLS = "calls";
    }
}
