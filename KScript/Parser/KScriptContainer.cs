using KScript.Arguments;
using KScript.Handlers;
using KScript.KScriptDocument;
using KScript.KScriptObjects;
using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace KScript
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptContainer
    {
        //Constant to store Assembly path for KScript Arguments.
        const string ASSEMBLY_PATH = "KScript.Arguments";
        const string VARIABLE_ASSEMBLY_PATH = "KScript.VariableFunctions";

        //Property to store the random object used to generate random numbers and digits.
        private readonly Random _random;

        //StringHandler property used to store the KScriptStringHandler class.
        private KScriptStringHandler StringHandler { get; }

        //Properties used to store file path and file directory of opened Script.
        internal string FilePath { get; set; }
        internal string FileDirectory { get; set; }

        //Dictionary list of Defs used for retrieval of defs within the script.
        internal IDictionary<string, def> defs { get; set; }

        //Used to store arrays
        internal IDictionary<string, List<string>> arrays { get; set; }

        //Dictionary list of Types of loaded KScript Objects.
        internal Dictionary<string, Type> LoadedKScriptObjects { get; set; }

        //Property used to retrieve the value of the _random property.
        internal Random GetRandom() => _random;

        //Used to store KScript properties.
        internal KScriptProperties Properties { get; } = null;

        //Property used to store the parser class.
        internal KScriptParser Parser { get; private set; }

        internal IDictionary<string, Type> LoadedVariableFunctions { get; set; }

        //Boolean used to determine if a type has a default constructor.
        internal bool HasDefaultConstructor(Type t) => t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null;

        //Property to store whether execution of the KScript document is allowed.
        internal bool AllowExecution { get; set; }

        //Methods to handle console output using the string handler class.
        internal void Out(string value) { Console.WriteLine(StringHandler.Format(value)); }
        internal void Out(object value) { Console.WriteLine(StringHandler.Format(value.ToString())); }
        internal void Out(object value, params string[] args) { Console.Write(value.ToString(), args); }
        internal void Out() { Console.WriteLine(); }

        //Methods to handle retrieving file and directories.
        public string GetFilePath() => FilePath;
        public string GetFileDirectory() => FileDirectory;


        internal KScriptObjectStorageContainer ObjectStorageContainer;

        //Method to halt KScript parsing.
        public bool Stop() { AllowExecution = false; return AllowExecution; }

        /// <summary>
        /// Class to handle printing out KScript information such as commands, versions etc.
        /// </summary>
        public void PrintInfo()
        {
            Out("About KScript");
            Out("-----------------------------------------------");
            Out("Version: 0.0.0.1 (Alpha)");
            Out("Supported Commands:");

            var q = from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.IsClass && typeof(KScriptObject).IsAssignableFrom(t) && t.Namespace == "KScript.Arguments"
                    select t;


            IndentedTextWriter indentedTextWriter = new IndentedTextWriter(Console.Out);
            indentedTextWriter.Indent = 2;

            foreach (var t in q.ToList())
            {
                bool HideClass = t.GetCustomAttributes<KScriptHideObject>().Any();
                bool HasNoInnerObjects = t.GetCustomAttributes<KScriptNoInnerObjects>().Any();

                if (!HideClass)
                {
                    indentedTextWriter.Indent = 0;
                    indentedTextWriter.WriteLine();

                    for (int i = 0; i < Console.WindowWidth; i++)
                    {
                        indentedTextWriter.Write("=");
                    }

                    indentedTextWriter.WriteLine();

                    if (!HasNoInnerObjects)
                    {
                        indentedTextWriter.Write("< " + t.Name + " > ... </ " + t.Name + " >\n");
                    }
                    else
                    {
                        indentedTextWriter.Write("< " + t.Name + " />\n");
                    }

                    indentedTextWriter.Indent = 1;
                    indentedTextWriter.WriteLine();
                    indentedTextWriter.WriteLine("[ Usage ] ");
                    indentedTextWriter.Indent = 2;

                    indentedTextWriter.WriteLine("Object Contents: " + (HasNoInnerObjects ? "Does not require inner elements or content." : "Inner elements or content are required.") + "\n");

                    indentedTextWriter.WriteLine(string.Format(KScriptParser.GetScriptObject(t).UsageInformation()));
                    indentedTextWriter.WriteLine();

                    IEnumerable<PropertyInfo> properties = t.GetProperties().Where(p => p.CanWrite);

                    int index = 0;

                    if (properties.Any(p => p.GetCustomAttributes<KScriptProperty>().Any()))
                    {
                        indentedTextWriter.Indent = 1;
                        indentedTextWriter.WriteLine("[ Arguments ]");
                    }

                    foreach (PropertyInfo p in properties)
                    {
                        IEnumerable<KScriptProperty> Properties = p.GetCustomAttributes<KScriptProperty>(false);
                        foreach (KScriptProperty prop in Properties)
                        {
                            indentedTextWriter.Indent = 2;
                            indentedTextWriter.WriteLine("[" + ++index + "] - " + p.Name + (prop.IsRequired() ? " (required)" : ""));
                            indentedTextWriter.Indent = 3;
                            indentedTextWriter.WriteLine(prop.ToString());
                            IEnumerable<KScriptExample> Examples = p.GetCustomAttributes<KScriptExample>(false);
                            int example_count = 0;
                            if (Examples.Any())
                            {
                                indentedTextWriter.WriteLine("[ Examples ]");
                                foreach (KScriptExample example in Examples)
                                {
                                    indentedTextWriter.Indent = 4;
                                    indentedTextWriter.WriteLine(++example_count + " - " + example.ToString());
                                }
                            }

                            KScriptAcceptedOptions Accepted_Value = p.GetCustomAttribute<KScriptAcceptedOptions>(false);

                            if (Accepted_Value != null)
                            {
                                indentedTextWriter.Indent = 3;
                                indentedTextWriter.WriteLine("[ Accepted Values ]");

                                int val_count = 0;
                                foreach (var item in Accepted_Value.GetValues())
                                {
                                    indentedTextWriter.Indent = 4;
                                    indentedTextWriter.WriteLine(++val_count + " - " + item);
                                }
                            }
                        }
                    }

                    indentedTextWriter.WriteLine();
                }
            }

            Out();
            Out();
        }

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
        }


        /// <summary>
        /// Allows any KScript Object to handle internal commands and requests.
        /// </summary>
        /// <param name="req">Requested property</param>
        /// <param name="val">New value</param>
        internal void HandleInternalCommandRequest(string req, string val)
        {
            //switch (req)
            //{
            //    default:
            //        break;
            //}
            throw new NotImplementedException();
        }

        /// <summary>
        /// Method used to load built in types to KScript object list.
        /// </summary>
        internal void LoadBuiltInTypes()
        {
            var q = from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.IsClass && t.Namespace == ASSEMBLY_PATH
                    select t;
            q.ToList().ForEach(i => AddKScriptObjectType(i));
        }


        /// <summary>
        /// Method used to load built in variable types to KScript variable functions list.
        /// </summary>
        internal void LoadBuiltInVariableFunctionTypes()
        {
            var q = from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.IsClass && t.Namespace == VARIABLE_ASSEMBLY_PATH
                    select t;
            q.ToList().ForEach(i => AddVariableFunctionType(i));
        }


        /// <summary>
        /// Used to add a type to the KScript loaded Objects dictionary list
        /// </summary>
        /// <param name="t">The type to add</param>
        internal void AddKScriptObjectType(Type t)
        {
            if (LoadedKScriptObjects.ContainsKey(t.Name.ToLower()))
            {
                LoadedKScriptObjects[t.Name.ToLower()] = t;
            }
            else
            {
                LoadedKScriptObjects.Add(t.Name.ToLower(), t);
            }
        }

        /// <summary>
        /// Used to add a type to the KScript loaded variable functions dictionary list
        /// </summary>
        /// <param name="t">The type to add</param>
        internal void AddVariableFunctionType(Type t)
        {
            if (LoadedVariableFunctions.ContainsKey(t.Name.ToLower()))
            {
                LoadedVariableFunctions[t.Name.ToLower()] = t;
            }
            else
            {
                LoadedVariableFunctions.Add(t.Name.ToLower(), t);
            }
        }


        /// <summary>
        /// Method used to handle exceptions - current purpose is to output the exception message.
        /// </summary>
        /// <param name="ex"></param>
        internal void HandleException(Exception ex) => Out(ex);

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
        public List<string> ArrayGet(string id) => arrays[id];


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
            get
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return defs[id];
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if (defs.ContainsKey(id))
                {
                    throw new KScriptException("KScriptDefInUse", string.Format("A def with the id '{0}' already exists.", id));
                }
                else
                {
                    defs.Add(new KeyValuePair<string, def>(id, value));
                }
            }
        }
    }
}
