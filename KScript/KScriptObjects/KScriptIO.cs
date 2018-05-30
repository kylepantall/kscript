using KScript.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.KScriptTypes
{
    public class KScriptIO : Object
    {
        public KScriptIO() { }
        public KScriptIO(KScriptContainer container) => ParentContainer = container;
        public KScriptContainer ParentContainer { get; private set; }
        public void SetContainer(KScriptContainer container) => ParentContainer = container;
        public void AddType(Type val) => ParentContainer.LoadedKScriptObjects.Add(val.Name.ToLower(), val);
        public bool ToBool(string @in) => KScriptBoolHandler.Convert(@in);
        public string ToBoolString(bool @in) => @in ? "yes" : "no";
        public void Out() => Console.Out.WriteLine();
        public void Out(string val) => Console.Out.Write(KScriptCommandHandler.HandleCommands(ParentContainer.StringHandler.Format(val), ParentContainer));
        public string In() => Console.In.ReadLine();
        public string In(string prompt) { Out(prompt); return Console.In.ReadLine(); }
        public int InNumber() => int.Parse(Console.ReadLine().Trim());
        public int InNumber(string prompt) { Out(prompt); return int.Parse(Console.ReadLine().Trim()); }
    }
}
