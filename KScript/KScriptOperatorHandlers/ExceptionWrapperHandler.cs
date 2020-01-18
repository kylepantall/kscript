using System.Collections.Generic;
using KScript.Arguments;
using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class ExceptionWrapperHandler : OperatorHandler
    {
        public ExceptionWrapperHandler(KScriptContainer container) : base(container) { }

        public override bool CanRun(KScriptObject obj)
        {
            return typeof(KScriptExceptionWrapper).IsAssignableFrom(obj.GetType());
        }

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptExceptionWrapper objX = obj as KScriptExceptionWrapper;
            Container.GetObjectStorageContainer().Add(Global.GlobalIdentifiers.EXCEPTIONS, objX, objX.type, Nodes);
        }
    }
}
