using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace KScript.Document
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptDocumentNode : IKScriptDocumentNode
    {
        private readonly KScriptObject Value;
        public KScriptObject GetValue() => Value;

        public List<IKScriptDocumentNode> Nodes { get; set; }

        public bool Ignore { get; set; } = false;
        public bool Continue { get; set; } = true;

        public void Run(KScriptContainer container, string args)
        {
            if (!Ignore && container.AllowExecution)
            {
                try { GetValue().Run(); }
                catch (KScriptTypes.KScriptExceptions.KScriptSkipScriptObject) { }
            }
        }
        public KScriptDocumentNode(KScriptObject obj) => Value = obj;
    }
}
