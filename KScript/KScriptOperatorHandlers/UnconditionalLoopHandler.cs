using System;
using System.Collections.Generic;
using KScript.Arguments;
using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class UnconditionalLoopHandler : OperatorHandler
    {
        public UnconditionalLoopHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj)
        {
            if (obj == null)
                return false;

            return typeof(KScriptUnconditionalLoop).IsAssignableFrom(obj.GetType());
        }

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptLoopConditional obj_x = obj as KScriptLoopConditional;

            do
            {
                foreach (IKScriptDocumentNode node in Nodes)
                {
                    if (node.GetValue().GetType().IsAssignableFrom(typeof(@break)))
                    {
                        Container.StopConditionalLoops();
                        break;
                    }

                    if (!Container.GetConditionalLoops())
                    {
                        node.Run(Container, null, obj);
                    }
                }

                if (Container.GetConditionalLoops())
                {
                    Container.AllowConditionalLoops();
                    return;
                }
            } while (true);
        }
    }
}
