using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.KScriptTypes.KScriptExceptions
{
    /// <summary>
    /// Used amongst all exceptions - general classification and polymorphism.
    /// </summary>
    public class KScriptException : Exception
    {
        public KScriptException() : base() { }
        public KScriptException(string message) : base(message) { }
    }

    public class KScriptSkipScriptObject : KScriptException { }

    /// <summary>
    /// Used to inform parser that a run method is not needed.
    /// </summary>
    public class KScriptNoRunMethodImplemented : KScriptSkipScriptObject { }

    /// <summary>
    /// Used to inform end-user that script type is invalid.
    /// </summary>
    public class KScriptInvalidScriptType : KScriptValidationException { }


    /// <summary>
    /// Ensures the parser doesn't validate this KScriptObject.
    /// </summary>
    public class KScriptNoValidationNeeded : KScriptValidationException { }


    /// <summary>
    /// To catch any exception which occurs from KScriptObject.Validate();
    /// </summary>
    public class KScriptValidationException : KScriptException { }
}
