using KScript.Arguments;
using KScript.Handlers;
using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace KScript
{
    public class KScriptContainer
    {
        public KScriptStringHandler StringHandler { get; }

        public IDictionary<string, def> defs { get; set; }
        public List<@if> Ifs { get; set; }
        public List<echo> echos { get; set; }

        private Random _random;
        public Random GetRandom() => _random;
        public KScriptProperties Properties { get; } = null;

        public bool HasDefaultConstructor(Type t) => t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null;
        public bool AllowExecution { get; set; } = true;
        public bool Stop() { AllowExecution = false; return AllowExecution; }

        public void PrintInfo()
        {
            Console.WriteLine("About KScript");
            Console.WriteLine("-----------------------------------------------");
            Console.WriteLine("Version: 0.0.0.1 (Alpha)");
            Console.WriteLine("Supported Commands:");

            var q = from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.IsClass && t.Namespace == "KScript.Arguments"
                    select t;

            IndentedTextWriter indentedTextWriter = new IndentedTextWriter(Console.Out);
            indentedTextWriter.Indent = 2;

            foreach (var t in q.ToList())
            {
                Console.WriteLine("- [" + t.Name + "]");
                try
                {
                    string usage_info = (HasDefaultConstructor(t) ? (KScriptObject)Activator.CreateInstance(t) : (KScriptObject)Activator.CreateInstance(t, "")).UsageInformation();
                    indentedTextWriter.WriteLine(usage_info + "\n");
                    indentedTextWriter.Flush();
                }
                catch (Exception) { }
            }

            Console.WriteLine();
            Console.WriteLine();
        }

        public KScriptContainer(KScriptProperties prop)
        {
            _random = new Random();
            defs = new Dictionary<string, def>();
            Ifs = new List<@if>();
            echos = new List<echo>();
            Properties = prop;
            StringHandler = new KScriptStringHandler(this);
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
