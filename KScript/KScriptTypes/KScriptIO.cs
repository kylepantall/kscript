using KScript.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.KScriptTypes
{
    public class KScriptIO
    {
        public KScriptIO() { }
        public KScriptIO(KScriptContainer container) => ParentContainer = container;

        public KScriptContainer ParentContainer { get; private set; }
        public void SetContainer(KScriptContainer container) => ParentContainer = container;

        public void Out() => Console.Out.WriteLine();
        public void Out(string val)
        {
            string _str = KScriptCommandHandler.HandleCommands(val, ParentContainer);
            _str = ParentContainer.StringHandler.Format(val);
            Console.Out.Write(_str);
        }

        public string In() => Console.In.ReadLine();
        public string In(string prompt) { Out(prompt); return Console.In.ReadLine(); }

        public int InNumber() => Console.In.Read();
        public int InNumber(string prompt) { Out(prompt); return Console.In.Read(); }
    }
}
