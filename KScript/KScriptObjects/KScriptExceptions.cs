using System;
using KScript.KScriptObjects;

namespace KScript.KScriptExceptions
{
    /// <summary>
    /// Used amongst all exceptions - general classification and polymorphism.
    /// </summary>
    [Serializable]
    public class KScriptException : Exception
    {
        public string ExceptionType;

        public KScriptBaseObject KScriptObject { get; set; }

        public KScriptException() { }
        public KScriptException(KScriptBaseObject obj) : base() { KScriptObject = obj; }
        public KScriptException(string exception_type, string message) : base(message)
        {
            ExceptionType = exception_type;
        }

        public KScriptException(string message) : base(message)
        {
            ExceptionType = GetType().Name;
        }

        public KScriptException(KScriptBaseObject obj, string message) : base(message) { KScriptObject = obj; }
    }

    /// <summary>
    /// Used to inform the parser to skip the method
    /// </summary>
    [Serializable]
    public class KScriptSkipScriptObject : KScriptException
    {
        public KScriptSkipScriptObject(KScriptBaseObject obj) : base(obj) { }
    }

    /// <summary>
    /// Used to inform parser that a run method is not needed.
    /// </summary>
    [Serializable]
    public class KScriptNoRunMethodImplemented : KScriptSkipScriptObject
    {
        public KScriptNoRunMethodImplemented(KScriptBaseObject obj) : base(obj) { }
    }

    /// <summary>
    /// Used to inform end-user that script type is invalid.
    /// </summary>
    [Serializable]
    public class KScriptInvalidScriptType : KScriptValidationException
    {
        public KScriptInvalidScriptType(KScriptBaseObject obj) : base(obj) { }
    }


    /// <summary>
    /// Ensures the parser doesn't validate this KScriptObject.
    /// </summary>
    [Serializable]
    public class KScriptNoValidationNeeded : KScriptValidationException
    {
        public KScriptNoValidationNeeded(KScriptBaseObject obj) : base(obj) { }
        public KScriptNoValidationNeeded(KScriptBaseObject obj, string msg) : base(obj, msg) { }
    }


    /// <summary>
    /// To catch any exception which occurs from KScriptObject.Validate();
    /// </summary>
    [Serializable]
    public class KScriptValidationException : KScriptException
    {
        public KScriptValidationException(KScriptBaseObject obj) : base(obj) { }
        public KScriptValidationException(KScriptBaseObject obj, string msg) : base(obj, msg) { }
    }


    public class KScriptValidationFail : KScriptException
    {
        public KScriptValidationFail(KScriptBaseObject obj) : base(obj, "Validation has failed for the given KScriptObject.") { }
        public KScriptValidationFail(KScriptBaseObject obj, string msg) : base(obj, msg) { }
    }
    public class KScriptDirectoryNotFound : KScriptException
    {
        public KScriptDirectoryNotFound(KScriptBaseObject obj) : base(obj, "The given directory could not be found.") { }
        public KScriptDirectoryNotFound(KScriptBaseObject obj, string msg) : base(obj, msg) { }
    }
    public class KScriptArrayNotFound : KScriptException
    {
        public KScriptArrayNotFound(KScriptBaseObject obj) : base(obj, "The array could not be found with the given ID") { }
        public KScriptArrayNotFound(KScriptBaseObject obj, string msg) : base(obj, msg) { }
    }
    public class KScriptBoolInvalid : KScriptException
    {
        public KScriptBoolInvalid() : base("Bool invalid") { }
        public KScriptBoolInvalid(KScriptBaseObject obj) : base(obj, "The bool value given is not valid.") { }
        public KScriptBoolInvalid(KScriptBaseObject obj, string msg) : base(obj, msg) { }
    }
    public class KScriptDefNotFound : KScriptException
    {
        public KScriptDefNotFound() : base("Given def does not exist.") { }
        public KScriptDefNotFound(KScriptBaseObject obj) : base(obj, "Given def does not exist.") { }
        public KScriptDefNotFound(KScriptBaseObject obj, string msg) : base(obj, msg) { }
    }
    public class KScriptDefInUse : KScriptException
    {
        public KScriptDefInUse() : base("The given def is already in use.") { }
        public KScriptDefInUse(KScriptBaseObject obj) : base(obj, "The given def is already in use.") { }
        public KScriptDefInUse(KScriptBaseObject obj, string msg) : base(obj, msg) { }
    }
    public class KScriptMissingAttribute : KScriptException
    {
        public KScriptMissingAttribute(KScriptBaseObject obj) : base(obj, "The given KScriptObject is missing an attribute.") { }
        public KScriptMissingAttribute(KScriptBaseObject obj, string msg) : base(obj, msg) { }
    }
}
