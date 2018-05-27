﻿using System;
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
            XmlNode node = Document.DocumentElement;

            KScriptContainer container = new KScriptContainer(Properties);
            KScriptDocument document = new KScriptDocument();
            KScriptObject rootElement = GetScriptObject(node);

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

        public Type GetObjectType(string name)
        {
            return Type.GetType(string.Format("{0}.{1}", ASSEMBLY_PATH, name), false, true);
        }

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
