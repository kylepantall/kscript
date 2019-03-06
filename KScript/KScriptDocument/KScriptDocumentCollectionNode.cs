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

        public void Run(KScriptContainer container, string args)
        {
            bool @continue = true;
            if (!Ignore && container.AllowExecution)
            {
                if (GetValue() != null)
                {
                    //Try and run, if can continue save this value to field @continue.
                    try
                    {
                        if (GetValue().ValidationType != KScriptObject.ValidationTypes.BEFORE_PARSING)
                        {
                            GetValue().Validate();
                        }
                        @continue = GetValue().Run();
                    }
                    catch (KScriptSkipScriptObject) { }
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
                                    if (node.GetValue().GetType().IsAssignableFrom(typeof(@break)))
                                    {
                                        container.StopConditionalLoops();
                                        break;
                                    }
                                    else
                                    {
                                        if (!container.GetConditionalLoops())
                                        {
                                            node.Run(container, null);
                                        }
                                    }
                                }

                                if (container.GetConditionalLoops())
                                {
                                    container.AllowConditionalLoops();
                                    return;
                                }
                            }
                            while (obj.ToBool(obj.HandleCommands(obj.condition)));
                        }
                    }


                    else if (typeof(KScriptObjectLoop).IsAssignableFrom(GetValue().GetType()))
                    {
                        KScriptObjectLoop obj = (KScriptObjectLoop)(GetValue());

                        if (!container.GetDefs().ContainsKey(obj.to))
                        {
                            container.AddDef(obj.to, new def("0"));
                        }



                        //for (int i = int.Parse(container[obj.to].Contents); obj.ToBool(obj.HandleCommands(obj.@while)); i = int.Parse(obj.HandleCommands(obj.math)))
                        //{
                        //    foreach (IKScriptDocumentNode node in Nodes)
                        //    {
                        //        if (node.GetValue().GetType().IsAssignableFrom(typeof(@break)))
                        //        {
                        //            container.StopConditionalLoops();
                        //            break;
                        //        }
                        //        else
                        //        {
                        //            if (!container.GetConditionalLoops())
                        //            {
                        //                node.Run(container, null);
                        //            }
                        //        }
                        //    }
                        //    if (container.GetConditionalLoops())
                        //    {
                        //        container.AllowConditionalLoops();
                        //        break;
                        //    }
                        //}

                        do
                        {
                            foreach (IKScriptDocumentNode node in Nodes)
                            {
                                if (node.GetValue().GetType().IsAssignableFrom(typeof(@break)))
                                {
                                    container.StopConditionalLoops();
                                    break;
                                }
                                else
                                {
                                    if (!container.GetConditionalLoops())
                                    {
                                        node.Run(container, null);
                                    }
                                }
                            }
                            if (container.GetConditionalLoops())
                            {
                                container.AllowConditionalLoops();
                                break;
                            }

                            string val = obj.HandleCommands(obj.math);
                            container[obj.to].Contents = val;

                        } while (obj.ToBool(obj.HandleCommands(obj.@while)));

                        container.RemoveDef(obj.to);
                    }

                    else if (typeof(KScriptMethodWrapper).IsAssignableFrom(GetValue().GetType()))
                    {
                        KScriptMethodWrapper obj = GetValue() as KScriptMethodWrapper;

                        //Add the method call to the Object Storage Container
                        container.GetObjectStorageContainer().Add(Global.GlobalIdentifiers.CALLS, obj, obj.name, Nodes);
                    }

                    else if (typeof(KScriptExceptionWrapper).IsAssignableFrom(GetValue().GetType()))
                    {
                        KScriptExceptionWrapper obj = GetValue() as KScriptExceptionWrapper;

                        container.GetObjectStorageContainer().Add(Global.GlobalIdentifiers.EXCEPTIONS, obj, obj.type, Nodes);
                    }


                    else if (typeof(KScriptArrayLoop).IsAssignableFrom(GetValue().GetType()))
                    {
                        KScriptArrayLoop obj = (KScriptArrayLoop)(GetValue());

                        foreach (string item in container.ArrayGet(obj, obj.from))
                        {
                            obj.CreateDef(obj.to, Global.Values.NULL);
                            obj.Def(obj.to).Contents = item;

                            foreach (IKScriptDocumentNode node in Nodes)
                            {
                                if (node.GetValue().GetType().IsAssignableFrom(typeof(@break)))
                                {
                                    container.StopConditionalLoops();
                                    break;
                                }
                                else
                                {
                                    if (!container.GetConditionalLoops())
                                    {
                                        node.Run(container, null);
                                    }
                                }
                            }
                            if (container.GetConditionalLoops())
                            {
                                container.AllowConditionalLoops();
                                break;
                            }
                        }
                    }

                    else
                    {
                        Nodes.ForEach(node => node.Run(container, null));
                    }

                }

            }

        }
    }
}
