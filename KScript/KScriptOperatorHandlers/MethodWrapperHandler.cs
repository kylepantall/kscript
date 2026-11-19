using System.Collections.Generic;

using KScript.Document;

namespace KScript.KScriptOperatorHandlers
{
    class MethodWrapperHandler : OperatorHandler
    {
        public MethodWrapperHandler(KScriptContainer container) : base(container) { }

        // Previously this matched KScriptObjectLoop, so <method> bodies ran inline and <for> loops
        // could be dispatched here and crash.
        public override bool CanRun(KScriptObject obj) => obj is KScriptMethodWrapper;

        public override void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container)
        {
            KScriptMethodWrapper objX = (KScriptMethodWrapper)obj;
            Container.GetObjectStorageContainer().Add(Global.GlobalIdentifiers.CALLS, objX, objX.name, Nodes);
        }
    }
}
