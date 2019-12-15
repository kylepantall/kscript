using System.Collections.Generic;

using KScript.Document;
using KScript.KScriptTypes;

namespace KScript.KScriptOperatorHandlers
{
    public abstract class OperatorHandler : KScriptIO
    {
        public OperatorHandler(KScriptContainer container) : base(container) { }
        public abstract void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container);
        public abstract bool CanRun(KScriptObject obj);
    }
}
