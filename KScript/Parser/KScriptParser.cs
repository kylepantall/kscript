﻿using System;
using System.IO;
using System.Text;
using System.Xml;
using KScript.Arguments;
using KScript.Document;
using KScript.KScriptExceptions;

namespace KScript
{
    public class KScriptParser
    {
        public KScriptProperties Properties { get; set; }

        public XmlDocument Document { get; set; }
        public KScriptParser() { Document = new XmlDocument(); Properties = new KScriptProperties(); }
        public KScriptParser(string filename) : this()
        {
            string xml = File.ReadAllText(filename);
            FilePath = filename;

            string _byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
            Document.LoadXml(xml);

            StartScriptTime = DateTime.Now;
        }

        public DateTime StartScriptTime { get; set; }
        public DateTime EndScriptTime { get; set; }

        public string[] CustomArguments { get; set; }

        /// <summary>
        /// Used to load a script from a specified location.
        /// </summary>
        /// <param name="location">The XML file to load the KScript from</param>
        /// <param name="fresh">Recreate container objects and document.</param>
        public void Load(string location, bool fresh)
        {
            if (fresh)
            {
                Document = new XmlDocument();
                Properties = new KScriptProperties();
            }

            FilePath = location;

            Document.PreserveWhitespace = true;
            Document.Load(location);

            StartScriptTime = DateTime.Now;
        }

        public string FilePath { get; set; }

        public Document.KScriptDocument KScriptDocument { get; set; }
        public KScriptContainer KScriptContainer { get; set; }

        public void Parse()
        {
            XmlNode node = Document.DocumentElement;

            //XmlNodeList refs = Document.DocumentElement.SelectNodes(".//ref");
            //XmlNodeList catches = Document.DocumentElement.SelectNodes(".//catch");

            KScriptContainer = new KScriptContainer(Properties, this);

            KScriptContainer.LoadBuiltInTypes();
            KScriptContainer.LoadBuiltInVariableFunctionTypes();

            KScriptDocument = new Document.KScriptDocument();
            KScriptObject rootElement = GetScriptObject(node, KScriptContainer);
            KScriptContainer.FilePath = FilePath;
            KScriptContainer.FileDirectory = Path.GetDirectoryName(FilePath);
            PrepareProperties(rootElement, node, KScriptContainer);
            KScriptDocumentCollectionNode collection = new KScriptDocumentCollectionNode(rootElement);
            Iterate(node, KScriptDocument, KScriptContainer, collection);
            KScriptDocument.AddChild(collection);
            KScriptDocument.Run(KScriptContainer);
            Complete();
        }

        public void Iterate(XmlNode node, Document.KScriptDocument doc, KScriptContainer container, KScriptDocumentCollectionNode docNode)
        {
            foreach (XmlNode item in node.ChildNodes)
            {
                if (item.NodeType != XmlNodeType.Comment || item.NodeType == XmlNodeType.Text)
                {
                    KScriptObject obj = GetScriptObject(item, container);
                    if (obj != null)
                    {
                        if (PrepareProperties(obj, item, container))
                        {
                            if (item.HasChildNodes)
                            {
                                KScriptDocumentCollectionNode newCollection = new KScriptDocumentCollectionNode(obj);
                                Iterate(item, doc, container, newCollection);
                                docNode.Nodes.Add(newCollection);
                            }
                            else
                            {
                                docNode.Nodes.Add(new KScriptDocumentNode(obj));
                            }
                        }
                    }
                }
            }
        }

        public static bool HasDefaultConstructor(Type t) => t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null;
        public Type GetObjectType(string name, KScriptContainer container) => container.LoadedKScriptObjects.ContainsKey(name) ? container.LoadedKScriptObjects[name] : null;
        public static KScriptObject GetScriptObject(Type type, string param = null) => HasDefaultConstructor(type) ? (KScriptObject)Activator.CreateInstance(type) : (KScriptObject)Activator.CreateInstance(type, param);
        public KScriptObject GetScriptObject(XmlNode node, KScriptContainer container)
        {
            Type _type = GetObjectType(node.Name.ToLower(), container);
            if (_type != null)
            {
                if (!HasDefaultConstructor(_type))
                {
                    return GetScriptObject(_type, node.InnerText);
                }
                else
                {
                    return GetScriptObject(_type);
                }
            }
            else
            {
                return null;
            }
        }

        public static IVariableFunction GetVariableFunction(Type type, KScriptContainer container, string def_id) => (IVariableFunction)Activator.CreateInstance(type, container, def_id);
        public Type GetVariableType(string variable_func) => KScriptContainer.LoadedVariableFunctions.ContainsKey(variable_func) ? KScriptContainer.LoadedVariableFunctions[variable_func] : null;

        public IVariableFunction GetVariableFunction(string variable_id,
            string variable_func,
            KScriptContainer container)
        {
            Type _type = GetVariableType(variable_func);
            return _type != null ? GetVariableFunction(_type, KScriptContainer, variable_id) : null;
        }

        public bool PrepareProperties(KScriptObject obj, XmlNode item, KScriptContainer container)
        {
            if (obj["Contents"] != null) { obj["Contents"] = item.InnerText; }
            foreach (XmlAttribute at in item.Attributes)
            {
                if (at.Name != "Contents")
                {
                    obj[at.Name.ToLower()] = at.Value;
                }
            }
            if (typeof(def).IsAssignableFrom(obj.GetType())) { container[((def)obj).id] = obj as def; }
            if (typeof(defm).IsAssignableFrom(obj.GetType()))
            {
                defm _obj = (defm)obj;
                string[] ids = _obj.ids.Split(',');
                foreach (string id in ids)
                {
                    container[id] = new def(_obj.Contents);
                }
            }

            obj.Init(container);


            if (obj.ValidationType != KScriptObject.ValidationTypes.DURING_PARSING)
            {
                try { obj.Validate(); }
                catch (KScriptValidationException ex)
                {
                    if (typeof(KScriptInvalidScriptType).IsAssignableFrom(ex.GetType()))
                    {
                        return false;
                    }
                }
            }

            if (obj.RunImmediately)
            {
                try
                {
                    obj.Run();
                }
                catch (Exception ex)
                {
                    container.HandleException(obj, ex);
                }
            }

            return true;
        }

        public void Complete()
        {
            EndScriptTime = DateTime.Now;
            TimeSpan comparison = EndScriptTime.Subtract(StartScriptTime);
            DateTime toDateObj = new DateTime(comparison.Ticks);
            if (!Properties.Quiet)
            {
                Console.Out.WriteLine("\nScript finished - {0}", toDateObj.ToString("HH:mm:ss"));
            }
        }
    }
}