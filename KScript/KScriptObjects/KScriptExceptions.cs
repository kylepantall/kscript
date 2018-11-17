using System;

namespace KScript.KScriptTypes.KScriptExceptions
{
    /// <summary>
    /// Used amongst all exceptions - general classification and polymorphism.
    /// </summary>
    [Serializable]
    public class KScriptException : Exception
    {
        public string ExceptionType;
        public KScriptException() : base() { }
        public KScriptException(string exception_type, string message) : base(message)
        {
            ExceptionType = exception_type;
        }

        public KScriptException(string message) : base(message)
        {
            ExceptionType = GetType().Name;
        }
    }

    /// <summary>
    /// Used to inform the parser to skip the method
    /// </summary>
    [Serializable]
    public class KScriptSkipScriptObject : KScriptException { }

    /// <summary>
    /// Used to inform parser that a run method is not needed.
    /// </summary>
    [Serializable]
    public class KScriptNoRunMethodImplemented : KScriptSkipScriptObject { }

    /// <summary>
    /// Used to inform end-user that script type is invalid.
    /// </summary>
    [Serializable]
    public class KScriptInvalidScriptType : KScriptValidationException { }


    /// <summary>
    /// Ensures the parser doesn't validate this KScriptObject.
    /// </summary>
    [Serializable]
    public class KScriptNoValidationNeeded : KScriptValidationException { }


    /// <summary>
    /// To catch any exception which occurs from KScriptObject.Validate();
    /// </summary>
    [Serializable]
    public class KScriptValidationException : KScriptException { }


    public class KScriptValidationFail : KScriptException
    {
        public KScriptValidationFail(string msg) : base(msg) { }
    }
    public class KScriptDirectoryNotFound : KScriptException
    {
        public KScriptDirectoryNotFound(string msg) : base(msg) { }
    }
    public class KScriptArrayNotFound : KScriptException
    {
        public KScriptArrayNotFound(string msg) : base(msg) { }
    }
    public class KScriptBoolInvalid : KScriptException
    {
        public KScriptBoolInvalid(string msg) : base(msg) { }
    }
    public class KScriptDefNotFound : KScriptException
    {
        public KScriptDefNotFound(string msg) : base(msg) { }
    }
    public class KScriptDefInUse : KScriptException
    {
        public KScriptDefInUse(string msg) : base(msg) { }
    }
    public class KScriptMissingAttribute : KScriptException
    {
        public KScriptMissingAttribute(string msg) : base(msg) { }
    }
}
