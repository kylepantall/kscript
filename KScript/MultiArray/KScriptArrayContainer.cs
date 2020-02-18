using System.Collections.Generic;

namespace KScript.MultiArray
{
    public class KScriptArrayContainer
    {
        private readonly Dictionary<string, ArrayBase> Arrays;
        public KScriptArrayContainer() => Arrays = new Dictionary<string, ArrayBase>();

        public KScriptArrayContainer AddArray(string key, ArrayBase val)
        {
            Arrays.Add(key, val);
            return this;
        }

        public KScriptArrayContainer RemoveArrayIfExists(string key)
        {
            if (Arrays.ContainsKey(key))
            {
                Arrays.Remove(key);
            }

            return this;
        }

        public bool ContainsKey(string key) => Arrays.ContainsKey(key);

        public ArrayBase this[string key]
        {
            get
            {
                if (Arrays.ContainsKey(key))
                {
                    return Arrays[key];
                }
                else
                {
                    return null;
                }
            }
        }
    }
}
