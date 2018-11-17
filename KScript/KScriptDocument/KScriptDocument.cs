using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace KScript.Document
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptDocument
    {
        private readonly List<IKScriptDocumentNode> nodes;
        public KScriptDocument() => nodes = new List<IKScriptDocumentNode>();
        public List<IKScriptDocumentNode> Nodes() => nodes;
        public void AddChild(IKScriptDocumentNode obj) => Nodes().Add(obj);
        public IKScriptDocumentNode GetFirst() => Nodes().First();
        public void Run(KScriptContainer container)
        {
            try
            {
                foreach (var item in Nodes())
                {
                    if (container.AllowExecution)
                    {
                        item.Run(container, null);
                    }
                }
            }
            catch (Exception ex)
            {
                container.HandleException(ex);
            }
        }
    }
}
