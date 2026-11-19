using System.Collections.Generic;

using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class ConditionalLoopHandler : OperatorHandler
    {
        public ConditionalLoopHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj) => obj is KScriptLoopConditional;

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptLoopConditional obj_x = (KScriptLoopConditional)obj;

            while (Container.AllowExecution && obj_x.condition != null && obj_x.ToBool(obj_x.HandleCommands(obj_x.condition)))
            {
                if (!RunIteration(obj, Nodes, Container))
                {
                    return;
                }
            }
        }
    }
}
