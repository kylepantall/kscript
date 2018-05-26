using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.KScriptTypes.KScriptExceptions
{
    /// <summary>
    /// Used amongst all exceptions.
    /// </summary>
    class KScriptException : Exception
    {
        public KScriptException() : base() { }
        public KScriptException(string message) : base(message) { }
    }
    /// <summary>
    /// Used to inform parser that a run method is not needed.
    /// </summary>
    class KScriptNoRunMethodImplemented : KScriptException { }

    /// <summary>
    /// Used to inform end-user that script type is invalid.
    /// </summary>
    class KScriptInvalidScriptType : KScriptException { }


    class KScriptNoValidationNeeded : KScriptException { }
}
