using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Reflection;
using KScript.Arguments;
using KScript.KScriptTypes.KScriptExceptions;

namespace KScript
{
    class KScriptParser
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
            XmlNodeList node = Document.FirstChild.ChildNodes;

            KScriptContainer container = new KScriptContainer(Properties);

            foreach (XmlNode item in node)
            {
                if (!string.IsNullOrWhiteSpace(item.Name) && item.NodeType != XmlNodeType.Comment)
                {
                    Type _type = Assembly.GetExecutingAssembly().GetType(string.Format("{0}.{1}", ASSEMBLY_PATH, item.Name));

                    if (_type != null)
                    {
                        KScriptObject obj = (_type.GetConstructor(Type.EmptyTypes) != null) ? (KScriptObject)Activator.CreateInstance(_type) : (KScriptObject)Activator.CreateInstance(_type, item.InnerText);
                        if (obj != null)
                            HandleKScriptObject(item, container, obj);
                    }
                }
            }

            Complete();
        }

        public void HandleKScriptObject(XmlNode item, KScriptContainer container, KScriptObject obj)
        {
            ///Implement line number soon
            obj.Init(container, item.OuterXml);

            if (typeof(KScriptObjectEnumerable).IsAssignableFrom(obj.GetType()))
            {
                foreach (XmlNode child in item.ChildNodes)
                {
                    
                }
            }

            if (obj["contents"] != null) { obj["contents"] = item.InnerText; }

            foreach (XmlAttribute at in item.Attributes)
            {
                if (at.Name != "contents")
                    obj[at.Name.ToLower()] = at.Value;
            }

            if (typeof(def).IsAssignableFrom(obj.GetType()))
            {
                container[((def)obj).id] = obj as def;
            }

            try { obj.Validate(); } catch (KScriptNoValidationNeeded) { }
            try { obj.Run(); } catch (KScriptNoRunMethodImplemented) { }
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
