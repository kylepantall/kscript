using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

using KScript.Arguments;
using KScript.CommandHandler;
using KScript.Handlers;
using KScript.KScriptDocument;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;
using KScript.KScriptParserHandlers;
using KScript.MultiArray;

namespace KScript
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptContainer
    {
        private bool ConditionalLoops = true;

        //Property to store the random object used to generate random numbers and digits.
        private readonly Random _random;

        //StringHandler property used to store the KScriptStringHandler class.
        private KScriptStringHandler StringHandler { get; }

        /// <summary>
        /// Stores KScript Array Containers
        /// </summary>
        private readonly KScriptArrayContainer KScriptArrayContainer;

        //Properties used to store file path and file directory of opened Script.
        internal string FilePath { get; set; }
        internal string FileDirectory { get; set; }

        //Dictionary list of Defs used for retrieval of defs within the script.
        private IDictionary<string, def> defs { get; set; }

        //Stores the commands used for each KScriptObject session.
        private readonly Dictionary<string, ICommandObject> CommandStore = new Dictionary<string, ICommandObject>();

        //Stores all the constant KScriptObject properties - prefixed with '__'.
        private readonly Dictionary<string, string> ConstantProperties = new Dictionary<string, string>();

        #region Global Values Storage - Stores values retrievable across all KScriptObjects (where ParentContainer accessible)
        private readonly Dictionary<string, Dictionary<string, string>> UniqueStore = new Dictionary<string, Dictionary<string, string>>();

        public void ClearGlobalValues(string key) => UniqueStore.Remove(key);

        /// <summary>
        /// Adds a global value at the key and ID
        /// </summary>
        /// <param name="key">Key of the global variables.</param>
        /// <param name="id">ID for the value.</param>
        /// <param name="value">Value to store at the given ID.</param>
        public void AddGlobalValue(string key, string id, string value)
        {
            var globalValue = GetGlobalValues(key);

            if (globalValue != null && globalValue.ContainsKey(id))
                GetGlobalValues(key).Remove(id);

            if (GetGlobalValues(key) == null)
                UniqueStore.Add(key, new Dictionary<string, string>());

            GetGlobalValues(key).Add(id, value);
        }

        /// <summary>
        /// Returns the value stored within the Key and with the given ID
        /// If no global value is found, the null string is returned
        /// </summary>
        /// <param name="key">Key to search within</param>
        /// <param name="id">ID to retrieve value from</param>
        /// <returns></returns>
        public string GetGlobalValue(string key, string id) => GetGlobalValues(key).ContainsKey(id) ? GetGlobalValues(key)[id] : Global.Values.NULL;


        /// <summary>
        /// Determines if the Key exists with the given ID also.
        /// </summary>
        /// <param name="key">Key to check if exists.</param>
        /// <param name="id">ID to check if existent within the list with the given Key.</param>
        /// <returns>True if exists, false if otherwise.</returns>
        public bool HasGlobalValue(string key, string id) => UniqueStore.ContainsKey(key) && UniqueStore[key].ContainsKey(id);

        /// <summary>
        /// Returns global values with the given key
        /// </summary>
        /// <param name="key">The values for a unique ID</param>
        /// <returns>The dictionary list of values for the given key</returns>
        public Dictionary<string, string> GetGlobalValues(string key)
        {
            if (!UniqueStore.ContainsKey(key))
            {
                UniqueStore.Add(key, new Dictionary<string, string>());
            }
            return UniqueStore[key];
        }
        #endregion

        //Method used to obtain the Command store dictionary.
        public Dictionary<string, ICommandObject> GetCommandStore() => CommandStore;

        /// <summary>
        /// Used to retrieve constant KScript Properties - prefixed with '__', registers value + ID.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, string> GetConstantProperties() => ConstantProperties;

        /// <summary>
        /// Method used to retrieve defs dictionary.
        /// </summary>
        /// <returns>All defs as a dictionary</returns>
        internal IDictionary<string, def> GetDefs() => defs;

        /// <summary>
        /// Method to determine if the given def ID exists already.
        /// </summary>
        /// <param name="id">Def ID to check</param>
        /// <returns>If the Def exists</returns>
        internal bool HasDef(string id) => defs.ContainsKey(id);


        /// <summary>
        /// Method used to obtain a def with the given id.
        /// If dynamic def creation is enabled, when not found, the def will be generated. 
        /// </summary>
        /// <param name="id">ID of the def</param>
        internal def GetDef(string id, string defaultValue = null)
        {
            if (defs.ContainsKey(id))
            {
                return defs[id];
            }

            if (Properties.DynamicDefs)
            {
                defs[id] = (defaultValue == null) ? new def() : new def(defaultValue);
                return defs[id];
            }

            throw new KScriptDefNotFound(null, $"The def with the id '{id}' could not be found.");
        }

        /// <summary>
        /// Used to prevent conditional loops - numerical or just conditional
        /// </summary>
        internal void StopConditionalLoops() => ConditionalLoops = false;

        /// <summary>
        /// Used to restore conditional and numerical loops
        /// </summary>
        internal void AllowConditionalLoops() => ConditionalLoops = true;

        /// <summary>
        /// Used to retrieve the conditional loops bool
        /// </summary>
        /// <returns></returns>
        internal bool GetConditionalLoops() => !ConditionalLoops;

        /// <summary>
        /// Method used to add def to def dictionary
        /// </summary>
        /// <param name="key">Key to use</param>
        /// <param name="def">Def object to add</param>
        internal void AddDef(string key, def def, KScriptObject parent = null)
        {
            if (parent != null)
            {
                def.SetBaseScriptObject(parent);
                def.SetContainer(this);
            }
            defs.Add(key, def);
        }

        /// <summary>
        /// Method used to remove a def from def dictionary
        /// </summary>
        /// <param name="key">Key to use</param>
        internal void RemoveDef(string key) => defs.Remove(key);

        //Used to store arrays
        private IDictionary<string, List<string>> arrays { get; set; }

        //Dictionary list of Types of loaded KScript Objects.
        internal Dictionary<string, Type> LoadedKScriptObjects { get; set; }

        internal Dictionary<string, Type> LoadedParserHandlers { get; set; }

        internal Dictionary<string, Type> LoadedOperatorHandlers { get; set; }

        //Property used to retrieve the value of the _random property.
        internal Random GetRandom() => _random;

        //Used to store KScript properties.
        internal KScriptProperties Properties { get; } = null;

        //Property used to store the parser class.
        internal KScriptParser Parser { get; private set; }

        internal IDictionary<string, Type> LoadedVariableFunctions { get; set; }

        //Boolean used to determine if a type has a default constructor.
        internal bool HasDefaultConstructor(Type t) => t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null;

        /// <summary>
        /// Property used to store whether execution of the KScript document is allowed.
        /// </summary>
        /// <value>default is true</value>
        internal bool AllowExecution { get; set; } = true;

        //Methods to handle console output using the string handler class.
        internal void Out(string value) { HandleOutputEvent(value); Console.WriteLine(StringHandler.Format(value)); }
        internal void Out(object value) { HandleOutputEvent(value.ToString()); Console.WriteLine(StringHandler.Format(value.ToString())); }
        internal void Out(object value, params string[] args) { HandleOutputEvent(string.Format(value.ToString(), args)); Console.Write(value.ToString(), args); }
        internal void Out() { HandleOutputEvent("\n"); Console.WriteLine(); }

        /// <summary>
        /// Events used to capture output using the KScript Out Method.
        /// </summary>
        /// <param name="value"></param>
        public delegate void OnOutput(string value);
        public event OnOutput OnOutputEvent;

        public void HandleOutputEvent(string value) => OnOutputEvent?.Invoke(value);


        //Methods to handle retrieving file and directories.
        public string GetFilePath() => FilePath;
        public string GetFileDirectory() => FileDirectory;


        private readonly KScriptObjectStorageContainer ObjectStorageContainer;
        public KScriptObjectStorageContainer GetObjectStorageContainer() => ObjectStorageContainer;

        //Method to halt KScript parsing.
        public bool Stop() { AllowExecution = false; return AllowExecution; }

        /// <summary>
        /// Method to handle printing out KScript information such as commands, versions etc.
        /// </summary>
        public void PrintInfo()
        {
            KScriptObjectHelperWriter writer = new KScriptObjectHelperWriter();
            writer.Write(Parser);
        }

        /// <summary>
        /// Method used to retrieve multidimensional arrays stored within a KScript Script.
        /// </summary>
        /// <returns></returns>
        public KScriptArrayContainer GetMultidimensionalArrays() => KScriptArrayContainer;
        public void AddMultidimensionalArray(string key, ArrayBase val) => KScriptArrayContainer.AddArray(key, val);

        /// <summary>
        /// Constructor for KScriptContainer
        /// </summary>
        /// <param name="prop">Properties class to use for KScript parsing.</param>
        /// <param name="parser">The parser object to use.</param>
        public KScriptContainer(KScriptProperties prop, KScriptParser parser)
        {
            _random = new Random();
            defs = new Dictionary<string, def>();
            arrays = new Dictionary<string, List<string>>();
            Properties = prop;
            Parser = parser;
            AllowExecution = true;
            StringHandler = new KScriptStringHandler(this);
            LoadedKScriptObjects = new Dictionary<string, Type>();
            ObjectStorageContainer = new KScriptObjectStorageContainer();
            LoadedVariableFunctions = new Dictionary<string, Type>();
            LoadedParserHandlers = new Dictionary<string, Type>();
            LoadedOperatorHandlers = new Dictionary<string, Type>();
            KScriptArrayContainer = new KScriptArrayContainer();
        }

        /// <summary>
        /// Method used to load built in types to KScript object list.
        /// </summary>
        internal void LoadBuiltInTypes()
        {
            IEnumerable<Type> q = from t in Assembly.GetExecutingAssembly().GetTypes()
                                  where t.IsClass
                                  && t.Namespace != null
                                  && t.Namespace.StartsWith(Global.GlobalIdentifiers.ASSEMBLY_PATH)
                                  select t;
            q.ToList().ForEach(i => AddKScriptObjectType(i));
        }


        /// <summary>
        /// Method used to load built in variable types to KScript variable functions list.
        /// </summary>
        internal void LoadBuiltInVariableFunctionTypes()
        {
            IEnumerable<Type> q = from t in Assembly.GetExecutingAssembly().GetTypes()
                                  where t.IsClass && t.Namespace != null &&
                                  t.Namespace.StartsWith(Global.GlobalIdentifiers.VARIABLE_ASSEMBLY_PATH)
                                  select t;
            q.ToList().ForEach(i => AddVariableFunctionType(i));
        }

        /// <summary>
        /// Method used to load built in KScriptParser Handlers.
        /// </summary>
        internal void LoadBuiltInParserHandlers()
        {
            IEnumerable<Type> q = from t in Assembly.GetExecutingAssembly().GetTypes()
                                  where t.IsClass && t.Namespace != null
                                  && t.Namespace.StartsWith(Global.GlobalIdentifiers.PARSER_HANDLERS)
                                  && !t.IsAssignableFrom(typeof(IParserHandler))
                                  && !t.FullName.Contains("<")
                                  select t;
            q.ToList().ForEach(i => AddKScriptParserHandler(i));
        }

        /// <summary>
        /// Method used to load built in Node Handlers.
        /// </summary>
        internal void LoadBuiltInOperatorHandlers()
        {
            IEnumerable<Type> q = from t in Assembly.GetExecutingAssembly().GetTypes()
                                  where t.IsClass && t.Namespace != null
                                  && t.Namespace.StartsWith(Global.GlobalIdentifiers.OPERATOR_HANDLERS)
                                  && !t.IsAssignableFrom(typeof(KScriptOperatorHandlers.OperatorHandler))
                                  select t;
            q.ToList().ForEach(i => AddOperatorHandler(i));
        }

        /// <summary>
        /// Used to add a type to the KScript loaded Objects dictionary list
        /// </summary>
        /// <param name="t">The type to add</param>
        internal void AddKScriptObjectType(Type t) => LoadedKScriptObjects[t.Name.ToLower()] = t;

        internal void AddOperatorHandler(Type t) => LoadedOperatorHandlers[t.Name.ToLower()] = t;

        internal void AddKScriptParserHandler(Type t) => LoadedParserHandlers[t.Name.ToLower()] = t;

        /// <summary>
        /// Used to add a type to the KScript loaded variable functions dictionary list
        /// </summary>
        /// <param name="t">The type to add</param>
        internal void AddVariableFunctionType(Type t) => LoadedVariableFunctions[t.Name.ToLower()] = t;

        /// <summary>
        /// Method used to handle exceptions - current purpose is to output the exception message.
        /// </summary>
        /// <param name="ex"></param>
        internal void HandleException(KScriptBaseObject obj, Exception ex) => HandleException(new KScriptException(obj, ex.Message));

        /// <summary>
        /// Method used to handle exceptions - current purpose is to output the exception message.
        /// </summary>
        /// <param name="ex"></param>
        internal void HandleException(Exception ex)
        {
            if (!Properties.ThrowAllExceptions)
            {
                GetObjectStorageContainer().GetExceptionHandlers(ex.GetType().Name)
                           .ForEach(i => i.Run(this, null, i.GetValue()));

                if (typeof(KScriptException).IsAssignableFrom(ex.GetType()))
                {
                    KScriptException kex = (KScriptException)ex;

                    if (!GetObjectStorageContainer().GetExceptionHandlers(ex.GetType().Name).Any())
                        Out($"[error ~{kex.GetExceptionType()}: {kex.Message}] : {DateTime.Now.ToShortTimeString()}\n");
                    return;
                }

                Out($"[error ~{ex.GetType().Name}:{DateTime.Now.ToShortTimeString()}] {ex.Message}\n");
            }
        }


        /// <summary>
        /// Method used to handle exceptions - current purpose is to output the exception message.
        /// </summary>
        /// <param name="ex"></param>
        internal void HandleException(KScriptCommand obj, Exception ex) => HandleException(ex);

        /// <summary>
        /// Used to insert into the array with specified ID
        /// </summary>
        /// <param name="id">The ID to insert</param>
        /// <param name="list">The array to insert with specified id</param>
        public void ArrayInsert(string id, List<string> list) => arrays.Add(id, list);

        /// <summary>
        /// Used to retrieve an array with specified ID
        /// </summary>
        /// <param name="id">ID to retrieve array</param>
        /// <returns>Array of strings</returns>
        public List<string> ArrayGet(KScriptBaseObject obj, string id)
        {
            try
            {
                if (arrays.ContainsKey(id))
                {
                    return arrays[id];
                }

                throw new KScriptDefNotFound(obj, string.Format("The Array '{0}' does not exist.", id));
            }
            catch (Exception ex)
            {
                HandleException(ex);
                return null;
            }
        }


        /// <summary>
        /// Method used to retrieve the string handler
        /// </summary>
        /// <returns>KScriptStringHandler object</returns>
        public KScriptStringHandler GetStringHandler() => StringHandler;

        /// <summary>
        /// Method used to retrieve all arrays for the current KScript script.
        /// </summary>
        /// <returns>All arrays as a dictionary where the key is the array ID</returns>
        public IDictionary<string, List<string>> ArraysGet() => arrays;

        /// <summary>
        /// Method used to retrieve defs or add defs to the dictionary list with specified id.
        /// </summary>
        /// <param name="id">ID to retrieve or insert def in dictonary</param>
        /// <returns>Def with specified ID</returns>
        public def this[string id]
        {
            get => GetDef(id);
            set
            {
                if (!defs.ContainsKey(id))
                {
                    defs.Add(new KeyValuePair<string, def>(id, value));
                }

                defs[id] = value;
            }
        }
    }
}
