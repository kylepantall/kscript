using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using KScript.Document;
using KScript.Global;

namespace KScript.KScriptDocument
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptObjectStorageContainer
    {
        private readonly Dictionary<string, Dictionary<string, List<IKScriptDocumentNode>>> Value;
        private readonly Dictionary<string, object> ObjectStorage;

        public KScriptObjectStorageContainer()
        {
            Value = new Dictionary<string, Dictionary<string, List<IKScriptDocumentNode>>>();
            ObjectStorage = new Dictionary<string, object>();

            Value.Add(GlobalIdentifiers.CALLS, new Dictionary<string, List<IKScriptDocumentNode>>());
        }

        /**
         * Method used to retrieve a KScriptContainer with the given parent key and unique id. 
         * For instrance: 'calls' -> 'my_method' returns the KScriptObjects for this unique KScriptObjectWrapper.
         */
        public List<IKScriptDocumentNode> Get(string parent_key, string uid) => Value[parent_key][uid];

        /**
         * Method used to retrieve a KScriptObject which has been specifically added to the ObjectStorage container for
         * retrieval. For instance, the object can later be used to retrieve specific details of an operation.
         */
        public T GetObjectFromUID<T>(string uid) => (T)ObjectStorage[uid];

        public void AddObjectFromUID(string uid, object obj)
        {
            if (!ObjectStorage.ContainsKey(uid))
                ObjectStorage.Add(uid, obj);
        }

        /**
         * Inserts a value at the parent key, with the unique ID
         */
        public void Add(string at_parent_key, KScriptObject script_obj, string uid, List<IKScriptDocumentNode> values)
        {
            if (Value.ContainsKey(at_parent_key))
                Value[at_parent_key].Add(uid, values);

            if (!Value.ContainsKey(at_parent_key))
            {
                Dictionary<string, List<IKScriptDocumentNode>> obj = new Dictionary<string, List<IKScriptDocumentNode>>
                {
                    { uid, values }
                };
                Value.Add(at_parent_key, obj);
            }

            ObjectStorage.Add(uid, script_obj);
        }


        /// <summary>
        /// Returns all method names stored.
        /// </summary>
        /// <returns>Returns all method names</returns>
        public string[] GetMethodNames()
        {
            if (Value.ContainsKey(GlobalIdentifiers.CALLS))
                return Value[GlobalIdentifiers.CALLS].Select(i => i.Key).ToArray();
            return new string[0];
        }

        /**
         * Method used to retrieve a KScriptContainer with the given parent key and unique id. 
         * For instrance: 'calls' -> 'my_method' returns the KScriptObjects for this unique KScriptObjectWrapper.
         */
        public List<IKScriptDocumentNode> GetMethodCalls(string uid)
        {
            if (Value.ContainsKey(GlobalIdentifiers.CALLS) && Value[GlobalIdentifiers.CALLS].ContainsKey(uid))
                return Value[GlobalIdentifiers.CALLS][uid];
            return new List<IKScriptDocumentNode>();
        }

        /// <summary>
        /// Method used to retrieve Exception handlers defined using the 'onexception' KScriptObject.
        /// </summary>
        /// <param name="uid">Unique ID</param>
        /// <returns>List of KScriptDocument Nodes</returns>
        public List<IKScriptDocumentNode> GetExceptionHandlers(string uid)
        {
            if (Value.ContainsKey(GlobalIdentifiers.EXCEPTIONS) &&
                Value[GlobalIdentifiers.EXCEPTIONS].ContainsKey(uid))
                return Value[GlobalIdentifiers.EXCEPTIONS][uid];
            return new List<IKScriptDocumentNode>();
        }
    }
}
