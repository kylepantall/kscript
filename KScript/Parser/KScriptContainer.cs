using KScript.Arguments;
using KScript.Handlers;
using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace KScript
{
    public class KScriptContainer
    {
        const string ASSEMBLY_PATH = "KScript.Arguments";

        public KScriptStringHandler StringHandler { get; }

        public IDictionary<string, def> defs { get; set; }

        public string FilePath { get; set; }
        public string FileDirectory { get; set; }

        private Random _random;
        public Random GetRandom() => _random;
        public KScriptProperties Properties { get; } = null;

        public Dictionary<string, Type> LoadedKScriptObjects { get; set; }

        public KScriptParser Parser { get; private set; }

        public bool HasDefaultConstructor(Type t) => t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null;
        public bool AllowExecution { get; set; } = true;
        public bool Stop() { AllowExecution = false; return AllowExecution; }

        public void Out(string value) { Console.WriteLine(StringHandler.Format(value)); }
        public void Out() { Console.WriteLine(); }

        public void PrintInfo()
        {
            Out("About KScript");
            Out("-----------------------------------------------");
            Out("Version: 0.0.0.1 (Alpha)");
            Out("Supported Commands:");

            var q = from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.IsClass && t.Namespace == "KScript.Arguments"
                    select t;

            IndentedTextWriter indentedTextWriter = new IndentedTextWriter(Console.Out);
            indentedTextWriter.Indent = 2;

            foreach (var t in q.ToList())
            {
                Out("- [" + t.Name + "]");
                try
                {
                    string usage_info = (HasDefaultConstructor(t) ? (KScriptObject)Activator.CreateInstance(t) : (KScriptObject)Activator.CreateInstance(t, "")).UsageInformation();
                    indentedTextWriter.WriteLine(StringHandler.Format(usage_info + "\n"));
                    indentedTextWriter.Flush();
                }
                catch (Exception) { }
            }

            Out();
            Out();
        }

        public KScriptContainer(KScriptProperties prop, KScriptParser parser)
        {
            _random = new Random();
            defs = new Dictionary<string, def>();
            Properties = prop;
            Parser = parser;
            StringHandler = new KScriptStringHandler(this);
            LoadedKScriptObjects = new Dictionary<string, Type>();
        }

        public void LoadBuildInTypes()
        {
            var q = from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.IsClass && t.Namespace == ASSEMBLY_PATH
                    select t;
            q.ToList().ForEach(i => AddKScriptObjectType(i));
        }

        public void AddKScriptObjectType(Type t)
        {
            if (LoadedKScriptObjects.ContainsKey(t.Name.ToLower())) LoadedKScriptObjects[t.Name.ToLower()] = t;
            else LoadedKScriptObjects.Add(t.Name.ToLower(), t);
        }

        public def this[string id]
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(id)) return defs[id];
                else return null;
            }
            set
            {
                if (defs.ContainsKey(id)) throw new KScriptException(string.Format("A def with the id '{0}' already exists.", id));
                else defs.Add(new KeyValuePair<string, def>(id, value));
            }
        }
    }
}
