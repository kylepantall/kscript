using System;
using System.Collections.Generic;
using KScript.Arguments;
using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class ConditionalLoopHandler : OperatorHandler
    {
        public ConditionalLoopHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj)
        {
            if (obj == null)
                return false;

            return typeof(KScriptLoopConditional).IsAssignableFrom(obj.GetType());
        }

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptLoopConditional obj_x = obj as KScriptLoopConditional;
            if (obj_x.condition != null && obj_x.ToBool(obj_x.HandleCommands(obj_x.condition)))
            {
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
                } while (obj_x.ToBool(obj_x.HandleCommands(obj_x.condition)));
            }
        }
    }
}
