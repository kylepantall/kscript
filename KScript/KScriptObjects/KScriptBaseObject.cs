using KScript.KScriptTypes;
using System.Collections.Generic;

namespace KScript.KScriptObjects
{
    public class KScriptBaseObject : KScriptIO
    {
        private readonly Dictionary<string, object> StoredValues;
        public KScriptBaseObject() : base() => StoredValues = new Dictionary<string, object>();
        public KScriptBaseObject(KScriptContainer container) : base(container) => StoredValues = new Dictionary<string, object>();

        /// <summary>
        /// Returns the value store associated with this object.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, object> GetValueStore() => StoredValues;
    }
}
