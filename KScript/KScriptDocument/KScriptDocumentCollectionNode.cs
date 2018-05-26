using KScript.Document;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript.Document
{
    public class KScriptDocumentCollectionNode : IKScriptDocumentNode
    {
        private KScriptObject Value;
        public KScriptObject GetValue() => Value;

        public List<IKScriptDocumentNode> Nodes { get; set; }
        public KScriptDocumentCollectionNode(KScriptObject Val)
        {
            Value = Val;
            Nodes = new List<IKScriptDocumentNode>();
        }

        public void Run()
        {
            foreach (IKScriptDocumentNode node in Nodes) node.Run();
        }
    }
}
