using KScript.Document;
using KScript.Global;
using System.Collections.Generic;

namespace KScriptLib.KScriptDocument
{
    public class KScriptObjectStorageContainer
    {
        private readonly Dictionary<string, Dictionary<string, List<IKScriptDocumentNode>>> Value;

        public KScriptObjectStorageContainer() => Value = new Dictionary<string, Dictionary<string, List<IKScriptDocumentNode>>>();

        /**
         * Method used to retrieve a KScriptContainer with the given parent key and unique id. 
         * For instrance: 'calls' -> 'my_method' returns the KScriptObjects for this unique KScriptObjectWrapper.
         */
        public List<IKScriptDocumentNode> Get(string parent_key, string uid) => Value[parent_key][uid];

        /**
         * Inserts a value at the parent key, with the unique ID
         */
        public void Add(string at_parent_key, string uid, List<IKScriptDocumentNode> values)
        {
            if (Value.ContainsKey(at_parent_key))
            {
                Value[at_parent_key].Add(uid, values);
            }
            else
            {
                Dictionary<string, List<IKScriptDocumentNode>> obj = new Dictionary<string, List<IKScriptDocumentNode>>();
                obj.Add(uid, values);
                Value.Add(at_parent_key, obj);
            }
        }




        /**
         * Method used to retrieve a KScriptContainer with the given parent key and unique id. 
         * For instrance: 'calls' -> 'my_method' returns the KScriptObjects for this unique KScriptObjectWrapper.
         */
        public List<IKScriptDocumentNode> GetMethodCalls(string uid) => Value[GlobalIdentifiers.CALLS][uid];

    }
}
