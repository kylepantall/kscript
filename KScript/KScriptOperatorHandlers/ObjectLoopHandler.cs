using System.Collections.Generic;
using KScript.Arguments;
using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class ObjectLoophandler : OperatorHandler
    {
        public ObjectLoophandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj)
        {
            return typeof(KScriptObjectLoop).IsAssignableFrom(obj.GetType());
        }

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptObjectLoop obj_x = obj as KScriptObjectLoop;

            var def = Container.GetDef(obj_x.to, "0");
            obj_x.@default = def.Contents;

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
                        node.Run(Container, null, obj);
                }

                if (Container.GetConditionalLoops())
                {
                    Container.AllowConditionalLoops();
                    break;
                }

                string val = obj.HandleCommands(obj_x.math);
                Container[obj_x.to].Contents = val;

            } while (obj.ToBool(obj.HandleCommands(obj_x.@while)));

            Container[obj_x.to].Contents = obj_x.@default;
        }
    }
}
