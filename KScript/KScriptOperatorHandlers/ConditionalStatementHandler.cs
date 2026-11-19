using System.Collections.Generic;

using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class ConditionalStatementHandler : OperatorHandler
    {
        public ConditionalStatementHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj) => obj is KScriptConditional;

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            // The condition has already been evaluated by KScriptConditional.Run(); this handler is only
            // reached when it passed, so evaluating it again here would run any side effects twice.
            RunBlock(obj, Nodes, Container);
        }
    }
}
