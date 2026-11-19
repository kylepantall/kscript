using System.Collections.Generic;

using KScript.Document;
using KScript.KScriptExceptions;

namespace KScript.KScriptOperatorHandlers
{
    class ArrayLoopHandler : OperatorHandler
    {
        public ArrayLoopHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj) => obj is KScriptArrayLoop;

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptArrayLoop objX = (KScriptArrayLoop)obj;
            List<string> items = Container.ArrayGet(obj, objX.from);

            if (items == null)
            {
                return;
            }

            // Iterate over a copy so the loop body can safely modify the array.
            foreach (string item in items.ToArray())
            {
                obj.CreateDef(objX.to, Global.Values.NULL);
                obj.Def(objX.to).Contents = item;

                if (!RunIteration(obj, Nodes, Container))
                {
                    return;
                }
            }
        }
    }
}
