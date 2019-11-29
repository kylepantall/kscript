using System.Collections.Generic;
using KScript.Arguments;
using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class ConditionalLoopHandler : OperatorHandler
    {
        public override bool CanRun(KScriptObject obj)
        {
            return obj.GetType().IsAssignableFrom(typeof(KScriptConditional));
        }

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptConditional obj_x = (KScriptConditional)(obj);
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
