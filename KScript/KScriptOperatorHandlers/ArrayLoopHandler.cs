using System.Collections.Generic;
using KScript.Arguments;
using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class ArrayLoopHandler : OperatorHandler
    {
        public ArrayLoopHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj)
        {
            return typeof(KScriptArrayLoop).IsAssignableFrom(obj.GetType());
        }

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptArrayLoop objX = obj as KScriptArrayLoop;

            foreach (string item in Container.ArrayGet(obj, objX.from))
            {
                obj.CreateDef(objX.to, Global.Values.NULL);
                obj.Def(objX.to).Contents = item;

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
            }
        }
    }
}
