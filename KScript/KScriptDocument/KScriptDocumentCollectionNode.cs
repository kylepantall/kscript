using System.Net.Mime;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using KScript.Arguments;
using KScript.KScriptExceptions;

namespace KScript.Document
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptDocumentCollectionNode : IKScriptDocumentNode
    {
        protected KScriptObject Value;
        public KScriptObject GetValue() => Value;
        public List<IKScriptDocumentNode> Nodes { get; set; }
        public KScriptDocumentCollectionNode(KScriptObject Val) : this() => Value = Val;
        public KScriptDocumentCollectionNode() => Nodes = new List<IKScriptDocumentNode>();
        public bool Ignore { get; set; } = false;

        public void Run(KScriptContainer container, string args, KScriptObject Pobj)
        {
            bool @continue = true;
            if (!Ignore && container.AllowExecution)
            {
                if (GetValue() != null)
                {
                    try
                    {
                        if (GetValue().GetValidationType() != KScriptObject.ValidationTypes.BEFORE_PARSING)
                            GetValue().Validate();

                        GetValue().SetBaseScriptObject(Pobj);
                        @continue = GetValue().Run();
                    }
                    catch (KScriptSkipScriptObject) { }
                    catch (KScriptException ex)
                    {
                        if (container.Properties.ThrowAllExceptions)
                            throw ex;
                        container.HandleException(ex);

                    }
                }

                if (@continue)
                {
                    var operatorHandler = container.Parser.GetOperatorInterface(GetValue());

                    if (operatorHandler != null)
                    {
                        operatorHandler.Execute(GetValue(), Nodes, container);
                        return;
                    }

                    Nodes.ForEach(node => node.Run(container, null, GetValue()));
                }
            }

        }
    }
}
