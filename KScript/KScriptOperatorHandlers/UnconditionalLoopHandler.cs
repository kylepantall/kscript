using System.Collections.Generic;

using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class UnconditionalLoopHandler : OperatorHandler
    {
        public UnconditionalLoopHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj) => obj is KScriptUnconditionalLoop;

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            // Runs until a <break/> or <end/> is reached.
            while (RunIteration(obj, Nodes, Container)) { }
        }
    }
}
