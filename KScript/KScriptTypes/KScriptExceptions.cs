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
    class KScriptException : Exception
    {
        public KScriptException() : base() { }
        public KScriptException(string message) : base(message) { }
    }

    class KScriptSkipScriptObject : KScriptException { }

    /// <summary>
    /// Used to inform parser that a run method is not needed.
    /// </summary>
    class KScriptNoRunMethodImplemented : KScriptSkipScriptObject { }

    /// <summary>
    /// Used to inform end-user that script type is invalid.
    /// </summary>
    class KScriptInvalidScriptType : KScriptException { }


    /// <summary>
    /// Ensures the parser doesn't validate this KScriptObject.
    /// </summary>
    class KScriptNoValidationNeeded : KScriptException { }
}
