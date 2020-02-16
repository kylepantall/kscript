using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

using KScript.Arguments;
using KScript.Document;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;
using KScript.KScriptOperatorHandlers;
using KScript.KScriptParserHandlers;
using KScript.VariableFunctions;

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

            Document.PreserveWhitespace = false;
            Document.LoadXml(xml);

            StartScriptTime = DateTime.Now;
        }

        public void SetOutput(TextWriter obj) => Console.SetOut(obj);
        public void SetInput(TextReader obj) => Console.SetIn(obj);

        public void SetXML(string xml) => Document.LoadXml(xml);

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

        public KScriptDocumentCollectionNode RootElements { get; set; }

        public void Parse()
        {
            XmlNode node = Document.DocumentElement;

            KScriptContainer = new KScriptContainer(Properties, this);

            KScriptContainer.LoadBuiltInTypes();
            KScriptContainer.LoadBuiltInParserHandlers();
            KScriptContainer.LoadBuiltInOperatorHandlers();
            KScriptContainer.LoadBuiltInVariableFunctionTypes();

            KScriptDocument = new Document.KScriptDocument();
            KScriptObject rootElement = GetScriptObject(node, KScriptContainer);
            KScriptContainer.FilePath = FilePath;
            KScriptContainer.FileDirectory = Path.GetDirectoryName(FilePath);
            PrepareProperties(rootElement, node, KScriptContainer);
            RootElements = new KScriptDocumentCollectionNode(rootElement);


            Iterate(node, KScriptDocument, KScriptContainer, RootElements);
            KScriptDocument.AddChild(RootElements);

            KScriptDocument.Run(KScriptContainer);
            Complete();
        }

        public IParserHandler GetParserInterface(KScriptObject obj) => GetParserHandler(KScriptContainer.LoadedParserHandlers.Values.FirstOrDefault(i => GetParserHandler(i).IsAcceptedObject(obj)) ?? null);
        public OperatorHandler GetOperatorInterface(KScriptObject obj)
        {
            var objX = KScriptContainer.LoadedOperatorHandlers.Values.FirstOrDefault(i => GetOperatorHandler(i).CanRun(obj)) ?? null;
            return GetOperatorHandler(objX);
        }
        private void Iterate(XmlNode node, Document.KScriptDocument doc, KScriptContainer container, KScriptDocumentCollectionNode docNode)
        {
            foreach (XmlNode item in node.ChildNodes)
            {
                if (!(item.NodeType != XmlNodeType.Comment || item.NodeType == XmlNodeType.Text))
                    continue;

                KScriptObject obj = GetScriptObject(item, container);

                if (obj == null)
                    continue;

                if (!PrepareProperties(obj, item, container))
                    continue;

                if (item.HasChildNodes)
                {
                    IParserHandler parserHandler = GetParserInterface(obj);

                    if (parserHandler != null)
                    {
                        KScriptDocumentNode collection = new KScriptDocumentNode(parserHandler.GenerateKScriptObject(obj, item));
                        docNode.Nodes.Add(collection);
                        continue;
                    }

                    KScriptDocumentCollectionNode withoutParserCollection = new KScriptDocumentCollectionNode(obj);
                    Iterate(item, doc, container, withoutParserCollection);
                    docNode.Nodes.Add(withoutParserCollection);
                    continue;
                }

                docNode.Nodes.Add(new KScriptDocumentNode(obj));
                continue;
            }
        }

        private bool HasDefaultConstructor(Type t) => t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null;

        public Type GetObjectType(string name, KScriptContainer container) => container.LoadedKScriptObjects.ContainsKey(name) ? container.LoadedKScriptObjects[name] : null;
        public KScriptObject GetScriptObject(Type type, string param = null)
        {
            if (HasDefaultConstructor(type))
            {
                return ((KScriptObject)Activator.CreateInstance(type));
            }

            return ((KScriptObject)Activator.CreateInstance(type, param));
        }
        public KScriptObject GetScriptObject(XmlNode node, KScriptContainer container)
        {
            Type _type = GetObjectType(node.Name.ToLower(), container);
            if (_type != null)
            {
                if (!HasDefaultConstructor(_type))
                {
                    return GetScriptObject(_type, node.InnerText).SetNode(node);
                }

                return GetScriptObject(_type).SetNode(node);
            }

            return null;
        }

        private IParserHandler GetParserHandler(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (typeof(IParserHandler).IsAssignableFrom(type))
            {
                return (IParserHandler)Activator.CreateInstance(type, KScriptContainer);
            }
            return null;
        }

        private OperatorHandler GetOperatorHandler(Type type)
        {
            if (type == null)
                return null;

            if (typeof(OperatorHandler).IsAssignableFrom(type))
                return (OperatorHandler)Activator.CreateInstance(type, KScriptContainer);

            return null;
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


        public static ITiedVariableFunction GetTiedVariableFunction(Type type, KScriptContainer container, string first_id, string second_id) => (ITiedVariableFunction)Activator.CreateInstance(type, container, first_id, second_id);
        public ITiedVariableFunction GetTiedVariableFunction(string first_variable_id, string second_variable_id, string variable_func, KScriptContainer container)
        {
            Type _type = GetVariableType(variable_func);
            return _type != null ? GetTiedVariableFunction(_type, KScriptContainer, first_variable_id, second_variable_id) : null;
        }

        /// <summary>
        /// Prepares properties for KScript objects.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="item"></param>
        /// <param name="container"></param>
        /// <returns></returns>
        public bool PrepareProperties(KScriptObject obj, XmlNode item, KScriptContainer container)
        {
            if (obj["Contents"] != null) { obj["Contents"] = item.InnerText; }
            foreach (XmlAttribute at in item.Attributes)
            {
                if (at.Name != "Contents")
                {
                    if (at.Name.StartsWith("__"))
                    {
                        container.GetConstantProperties().Add(at.Name.Substring(2), at.Value);
                    }
                    else
                    {
                        obj[at.Name.ToLower()] = at.Value;
                    }
                }
            }
            if (typeof(def).IsAssignableFrom(obj.GetType()))
            {
                obj.SetContainer(container);
                if (item.ChildNodes.OfType<XmlCDataSection>().Count() > 0)
                {
                    obj["Contents"] = item.ChildNodes.OfType<XmlCDataSection>().ToArray()[0].Data;
                }
                else
                {
                    obj["Contents"] = item.InnerXml;
                }
                container[((def)obj).id] = obj as def;
            }
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

            if (typeof(KScriptIDObject).IsAssignableFrom(obj.GetType()))
            {
                ((KScriptIDObject)obj).RegisterObject();
            }

            if (obj.GetValidationType() != KScriptObject.ValidationTypes.DURING_PARSING)
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

        /// <summary>
        /// When the parsing and execution of the script is complete, this function is called.
        /// </summary>
        public void Complete()
        {
            EndScriptTime = DateTime.Now;
            TimeSpan comparison = EndScriptTime.Subtract(StartScriptTime);
            string comparisonTicks = new DateTime(comparison.Ticks).ToString("HH:mm:ss");
            if (!Properties.Quiet)
            {
                Console.Out.WriteLine($"\nScript finished - {comparisonTicks}\n");
            }
        }
    }
}