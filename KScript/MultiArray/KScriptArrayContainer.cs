using System.Collections.Generic;

namespace KScript.MultiArray
{
    public class KScriptArrayContainer
    {
        private readonly Dictionary<string, ArrayBase> Arrays;
        public KScriptArrayContainer() => Arrays = new Dictionary<string, ArrayBase>();

        public void AddArray(string key, ArrayBase val) => Arrays.Add(key, val);

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
