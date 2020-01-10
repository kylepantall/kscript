using System;
using System.Collections.Generic;
using KScript.Arguments;
using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class ConditionalStatementHandler : OperatorHandler
    {
        public ConditionalStatementHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj)
        {
            if (obj == null)
                return false;

            return typeof(KScriptConditional).IsAssignableFrom(obj.GetType());
        }

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptConditional obj_x = obj as KScriptConditional;

            if (obj_x.condition != null && obj_x.ToBool(obj_x.HandleCommands(obj_x.condition)))
            {
                foreach (IKScriptDocumentNode node in Nodes)
                {
                    if (node.GetValue().GetType().IsAssignableFrom(typeof(@break)))
                    {
                        Container.StopConditionalLoops();
                        return;
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
            }

        }
    }
}
