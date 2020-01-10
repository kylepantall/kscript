using System.Collections.Generic;
using KScript.Arguments;
using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class MethodWrapperHandler : OperatorHandler
    {
        public MethodWrapperHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj)
        {
            return typeof(KScriptObjectLoop).IsAssignableFrom(obj.GetType());
        }

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptMethodWrapper objX = obj as KScriptMethodWrapper;
            Container.GetObjectStorageContainer().Add(Global.GlobalIdentifiers.CALLS, objX, objX.name, Nodes);
        }
    }
}