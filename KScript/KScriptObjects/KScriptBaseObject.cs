using System.Collections.Generic;
using KScript.KScriptTypes;

namespace KScript.KScriptObjects
{
    public class KScriptBaseObject : KScriptIO
    {
        private readonly Dictionary<string, object> StoredValues;
        public KScriptBaseObject() : base() => StoredValues = new Dictionary<string, object>();
        public KScriptBaseObject(KScriptContainer container) : base(container) => StoredValues = new Dictionary<string, object>();

        public string GetPropertyValue(string property)
        {
            if (GetType().GetProperty(property) != null)
            {
                return (string)GetType().GetProperty(property).GetValue(this, null);
            }

            return NULL;
        }

        /// <summary>
        /// Returns the value store associated with this object.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, object> GetValueStore() => StoredValues;
    }
}
