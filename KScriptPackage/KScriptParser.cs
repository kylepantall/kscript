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
using System.IO;

namespace KScript
{
    public class KScriptParser
    {
        const string ASSEMBLY_PATH = "KScript.Arguments";

        public KScriptProperties Properties { get; set; }

        public Dictionary<string, Type> LoadedTypes;

        public XmlDocument Document { get; set; }
        public KScriptParser() { Document = new XmlDocument(); Properties = new KScriptProperties(); }
        public KScriptParser(String filename) : this() { FilePath = filename; Document.Load(filename); StartScriptTime = DateTime.Now; }

        public DateTime StartScriptTime { get; set; }
        public DateTime EndScriptTime { get; set; }

        public string FilePath { get; set; }

        public void Parse()
        {
            LoadedTypes = new Dictionary<string, Type>();
            LoadBuildInTypes();

            XmlNode node = Document.DocumentElement;

            KScriptContainer container = new KScriptContainer(Properties, this);
            KScriptDocument document = new KScriptDocument();
            KScriptObject rootElement = GetScriptObject(node);

            container.FilePath = FilePath;
            container.FileDirectory = Path.GetDirectoryName(FilePath);

            PrepareProperties(rootElement, node, container);

            KScriptDocumentCollectionNode collection = new KScriptDocumentCollectionNode(rootElement);

            try
            {
                Iterate(node, document, container, collection);
                document.AddChild(collection);
                document.Run(container);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            Complete();
        }

        public void Iterate(XmlNode node, KScriptDocument doc, KScriptContainer container, KScriptDocumentCollectionNode docNode)
        {
            foreach (XmlNode item in node.ChildNodes)
            {
                if (item.NodeType != XmlNodeType.Comment)
                {
                    KScriptObject obj = GetScriptObject(item);
                    if (obj != null)
                    {
                        bool @continue = PrepareProperties(obj, item, container);
                        if (@continue)
                        {
                            if (item.HasChildNodes)
                            {
                                KScriptDocumentCollectionNode newCollection = new KScriptDocumentCollectionNode(obj);
                                Iterate(item, doc, container, newCollection);
                                docNode.Nodes.Add(newCollection);
                            }
                            else docNode.Nodes.Add(new KScriptDocumentNode(obj));
                        }
                    }
                }
            }
        }

        public bool HasDefaultConstructor(Type t) => t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null;

        public void LoadBuildInTypes()
        {
            var q = from t in Assembly.GetExecutingAssembly().GetTypes()
                    where t.IsClass && t.Namespace == ASSEMBLY_PATH
                    select t;
            q.ToList().ForEach(i => LoadedTypes.Add(i.Name.ToLower(), i));
        }


        public Type GetObjectType(string name) => LoadedTypes[name];

        public KScriptObject GetScriptObject(Type type, string param = null) => HasDefaultConstructor(type) ? (KScriptObject)Activator.CreateInstance(type) : (KScriptObject)Activator.CreateInstance(type, param);
        public KScriptObject GetScriptObject(XmlNode node)
        {
            Type _type = GetObjectType(node.Name.ToLower());
            if (_type != null)
                if (!HasDefaultConstructor(_type)) return GetScriptObject(_type, node.InnerText);
                else return GetScriptObject(_type);
            else
                return null;
        }

        public bool PrepareProperties(KScriptObject obj, XmlNode item, KScriptContainer container)
        {
            if (obj["contents"] != null) { obj["contents"] = item.InnerText; }
            foreach (XmlAttribute at in item.Attributes)
            {
                if (at.Name != "contents") obj[at.Name.ToLower()] = at.Value;
            }
            if (typeof(def).IsAssignableFrom(obj.GetType())) { container[((def)obj).id] = obj as def; }
            obj.Init(container);
            try { obj.Validate(); }
            catch (KScriptValidationException ex)
            {
                if (typeof(KScriptInvalidScriptType).IsAssignableFrom(ex.GetType())) return false;
            }
            return true;
        }

        public void Complete()
        {
            EndScriptTime = DateTime.Now;
            TimeSpan comparison = EndScriptTime.Subtract(StartScriptTime);
            DateTime toDateObj = new DateTime(comparison.Ticks);
            if (!Properties.Quiet) Console.Out.WriteLine("\nScript finished - {0}", toDateObj.ToString("HH:mm:ss"));
        }
    }
}
