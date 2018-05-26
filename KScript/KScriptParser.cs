using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Reflection;
using KScript.Arguments;
using KScript.KScriptTypes.KScriptExceptions;
using KScript.Document;

namespace KScript
{
    public class KScriptParser
    {
        const string ASSEMBLY_PATH = "KScript.Arguments";

        public KScriptProperties Properties { get; set; }

        public XmlDocument Document { get; set; }
        public KScriptParser() { Document = new XmlDocument(); Properties = new KScriptProperties(); }
        public KScriptParser(String filename) : this() { Document.Load(filename); StartScriptTime = DateTime.Now; }

        public DateTime StartScriptTime { get; set; }
        public DateTime EndScriptTime { get; set; }

        public void Parse()
        {
            XmlNode node = Document.FirstChild;
            KScriptContainer container = new KScriptContainer(Properties);
            KScriptDocument document = new KScriptDocument();

            document.AddChild(Iterate(node, container, new KScriptDocumentCollectionNode(GetScriptObject(node))));

            foreach (XmlNode item in node.ChildNodes)
            {
                Iterate(document, item);
            }

            document.Run();

            Complete();
        }

        public void Iterate(KScriptDocument doc, XmlNode node)
        {
            foreach (XmlNode item in node.ChildNodes)
            {
                if (item.HasChildNodes)
                {
                    KScriptObject obj = GetScriptObject(item);
                    KScriptDocumentCollectionNode col = new KScriptDocumentCollectionNode(obj);
                    Iterate(doc, item, col);
                } else
                {
                    Console.WriteLine(node.ToString());
                }
            }
        }

        public void Iterate(KScriptDocument doc, XmlNode node, KScriptDocumentCollectionNode collection)
        {
            Console.WriteLine(collection.ToString());
        }

        public bool HasDefaultConstructor(Type t) => t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null;
        public Type GetObjectType(string name) => Type.GetType(string.Format("{0}.{1}", ASSEMBLY_PATH, name.ToLower()));
        public KScriptObject GetScriptObject(Type type, string param = null) => HasDefaultConstructor(type) ? (KScriptObject)Activator.CreateInstance(type) : (KScriptObject)Activator.CreateInstance(type, param);
        public KScriptObject GetScriptObject(XmlNode node)
        {
            Type _type = GetObjectType(node.Name);
            if (_type != null)
                if (HasDefaultConstructor(_type)) return GetScriptObject(_type, node.InnerText);
                else return GetScriptObject(_type);
            else
                return null;
        }


        public KScriptDocumentCollectionNode Iterate(XmlNode node, KScriptContainer container, KScriptDocumentCollectionNode co = null)
        {
            KScriptDocumentCollectionNode collection = new KScriptDocumentCollectionNode(GetScriptObject(node));
            foreach (XmlNode item in node.ChildNodes)
            {
                KScriptObject obj = GetScriptObject(item);

                PrepareProperties(obj, node, container);

                if (obj.GetScriptObjectType() == KScriptObject.ScriptType.ENUMERABLE)
                {
                    if (co != null) co.Nodes.Add(Iterate(item, container, collection));
                    else co.Nodes.Add(Iterate(item, container, new KScriptDocumentCollectionNode(obj)));
                }
                collection.Nodes.Add(new KScriptDocumentNode(obj));
            }
            return collection;
        }


        public void PrepareProperties(KScriptObject obj, XmlNode item, KScriptContainer container)
        {
            if (obj["contents"] != null) { obj["contents"] = item.InnerText; }
            foreach (XmlAttribute at in item.Attributes) { if (at.Name != "contents") obj[at.Name.ToLower()] = at.Value; }
            if (typeof(def).IsAssignableFrom(obj.GetType())) { container[((def)obj).id] = obj as def; }
            obj.Init(container);
            try { obj.Validate(); } catch (KScriptNoValidationNeeded) { }
        }


        //public void HandleKScriptObject(XmlNode item, KScriptContainer container, object _obj)
        //{
        //    KScriptObject obj = (KScriptObject)_obj;
        //    if (obj["contents"] != null) { obj["contents"] = item.InnerText; }
        //    foreach (XmlAttribute at in item.Attributes) { if (at.Name != "contents") obj[at.Name.ToLower()] = at.Value; }
        //    if (typeof(def).IsAssignableFrom(obj.GetType())) { container[((def)obj).id] = obj as def; }
        //    try { obj.Validate(); } catch (KScriptNoValidationNeeded) { }
        //    try { obj.Run(); } catch (KScriptSkipScriptObject) { }
        //}


        public void Complete()
        {
            EndScriptTime = DateTime.Now;
            TimeSpan comparison = EndScriptTime.Subtract(StartScriptTime);
            DateTime toDateObj = new DateTime(comparison.Ticks);
            if (!Properties.Quiet) Console.Out.WriteLine("\nScript finished - {0}", toDateObj.ToString("HH:mm:ss"));
        }
    }
}
