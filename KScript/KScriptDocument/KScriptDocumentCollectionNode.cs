using KScript.Document;
using KScript.KScriptTypes.KScriptExceptions;
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
        public KScriptDocumentCollectionNode(KScriptObject Val) : this() => Value = Val;
        public KScriptDocumentCollectionNode() => Nodes = new List<IKScriptDocumentNode>();
        public bool Ignore { get; set; } = false;

        public void Run()
        {
            bool @continue = true;
            if (!Ignore)
            {
                if (GetValue() != null)
                {
                    try { @continue = GetValue().Run(); } catch (KScriptSkipScriptObject) { }
                }
                if (@continue)
                    foreach (IKScriptDocumentNode node in Nodes) node.Run();
            }
        }
    }
}
