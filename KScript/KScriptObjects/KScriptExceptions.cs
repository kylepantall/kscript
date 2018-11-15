using System;

namespace KScript.KScriptTypes.KScriptExceptions
{
    /// <summary>
    /// Used amongst all exceptions - general classification and polymorphism.
    /// </summary>
    [Serializable]
    public class KScriptException : Exception
    {
        public KScriptException() : base() { }
        public KScriptException(string message) : base(message) { }
    }

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
}
