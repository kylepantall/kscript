using System.Collections.Generic;
using System.Linq;

namespace KScript.Document
{
    public class KScriptDocument
    {
        private List<IKScriptDocumentNode> nodes;
        public KScriptDocument() => nodes = new List<IKScriptDocumentNode>();
        public List<IKScriptDocumentNode> Nodes() => nodes;
        public void AddChild(IKScriptDocumentNode obj) => Nodes().Add(obj);
        public IKScriptDocumentNode GetFirst() => Nodes().First();
        public void Run(KScriptContainer container)
        {
            if (container.AllowExecution) Nodes().ForEach(item => item.Run());
        }
    }
}
