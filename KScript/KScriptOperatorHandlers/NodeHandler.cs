﻿using KScript.Document;
using System.Collections.Generic;

namespace KScript.KScriptOperatorHandlers
{
    public abstract class NodeHandler
    {
        public abstract void Execute(KScriptObject obj, List<IKScriptDocumentNode> Nodes, KScriptContainer Container);
        public abstract bool CanRun(KScriptObject obj);
    }
}
