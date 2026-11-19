using System.Collections.Generic;

using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class ObjectLoophandler : OperatorHandler
    {
        public ObjectLoophandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj) => obj is KScriptObjectLoop;

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptObjectLoop obj_x = (KScriptObjectLoop)obj;

            var def = Container.GetDef(obj_x.to, "0");
            obj_x.@default = def.Contents;

            try
            {
                // The condition is checked before every iteration (including the first), like a regular for loop.
                while (Container.AllowExecution && obj.ToBool(obj.HandleCommands(obj_x.@while)))
                {
                    if (!RunIteration(obj, Nodes, Container))
                    {
                        break;
                    }

                    Container[obj_x.to].Contents = obj.HandleCommands(obj_x.math);
                }
            }
            finally
            {
                // The loop variable is scoped to the loop: restore its previous value.
                Container[obj_x.to].Contents = obj_x.@default;
            }
        }
    }
}
