using KScript.KScriptTypes.KScriptExceptions;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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

        public void Run(KScriptContainer container, string args)
        {
            bool @continue = true;
            if (!Ignore && container.AllowExecution)
            {
                if (GetValue() != null)
                {
                    //Try and run, if can continue save this value to field @continue.
                    try { @continue = GetValue().Run(); } catch (KScriptSkipScriptObject) { }
                }



                if (@continue)
                {
                    if (typeof(KScriptConditional).IsAssignableFrom(GetValue().GetType()))
                    {
                        KScriptConditional obj = (KScriptConditional)(GetValue());
                        if (obj.condition != null && obj.ToBool(obj.HandleCommands(obj.condition)))
                        {
                            do
                            {
                                foreach (IKScriptDocumentNode node in Nodes)
                                {
                                    node.Run(container, null);
                                }
                            }
                            while (obj.ToBool(obj.HandleCommands(obj.condition)));
                        }
                    }


                    else if (typeof(KScriptObjectLoop).IsAssignableFrom(GetValue().GetType()))
                    {
                        KScriptObjectLoop obj = (KScriptObjectLoop)(GetValue());
                        do
                        {
                            foreach (IKScriptDocumentNode node in Nodes)
                            {
                                node.Run(container, null);
                            }

                            string val = obj.HandleCommands(obj.math);
                            obj.Def(obj.to).Contents = val;

                        } while (obj.ToBool(obj.HandleCommands(obj.@while)));
                    }

                    else if (typeof(KScriptMethodWrapper).IsAssignableFrom(GetValue().GetType()))
                    {
                        KScriptMethodWrapper obj = GetValue() as KScriptMethodWrapper;

                        //Add the method call to the Object Storage Container
                        container.ObjectStorageContainer.Add(Global.GlobalIdentifiers.CALLS, obj.name, Nodes);
                    }

                    else
                    {
                        foreach (IKScriptDocumentNode node in Nodes)
                        {
                            node.Run(container, null);
                        }
                    }

                }

            }
        }
    }
}
