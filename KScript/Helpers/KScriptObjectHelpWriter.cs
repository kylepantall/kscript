using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KScript;
using KScript.KScriptObjects;

namespace KScript
{
    public class KScriptObjectHelperWriter
    {

        public void Out(object value) => Console.WriteLine(string.Format(value.ToString()));
        public void Out() => Console.WriteLine();

        public void Write(KScriptParser parser)
        {
            Out("About KScript");
            Out("-----------------------------------------------");
            Out($"Version: {Assembly.GetExecutingAssembly().GetName().Version.ToString()}");
            Out("Supported Commands:");

            IEnumerable<Type> q = from t in Assembly.GetExecutingAssembly().GetTypes()
                                  where t.IsClass && typeof(KScriptObject).IsAssignableFrom(t) &&
                                  t.Namespace.StartsWith(KScript.Global.GlobalIdentifiers.ASSEMBLY_PATH)
                                  select t;

            IndentedTextWriter indentedTextWriter = new IndentedTextWriter(Console.Out) { Indent = 2 };

            foreach (Type t in q.ToList())
            {
                bool HideClass = t.GetCustomAttributes<KScriptHideObject>().Any();
                bool HasNoInnerObjects = t.GetCustomAttributes<KScriptNoInnerObjects>().Any();

                if (!HideClass)
                {
                    indentedTextWriter.Indent = 0;
                    indentedTextWriter.WriteLine();

                    for (int i = 0; i < Console.WindowWidth; i++)
                    {
                        indentedTextWriter.Write("=");
                    }

                    indentedTextWriter.WriteLine();

                    if (!HasNoInnerObjects)
                    {
                        indentedTextWriter.Write("< " + t.Name + " > ... </ " + t.Name + " >\n");
                    }
                    else
                    {
                        indentedTextWriter.Write("< " + t.Name + " />\n");
                    }

                    indentedTextWriter.Indent = 1;
                    indentedTextWriter.WriteLine();
                    indentedTextWriter.WriteLine("[ Usage ] ");
                    indentedTextWriter.Indent = 2;

                    indentedTextWriter.WriteLine("Object Contents: " + (HasNoInnerObjects ? "Does not require inner elements or content." : "Inner elements or content are required.") + "\n");

                    indentedTextWriter.WriteLine(string.Format(parser.GetScriptObject(t).UsageInformation()));
                    indentedTextWriter.WriteLine();

                    IEnumerable<PropertyInfo> properties = t.GetProperties().Where(p => p.CanWrite);

                    int index = 0;

                    if (properties.Any(p => p.GetCustomAttributes<KScriptProperty>().Any()))
                    {
                        indentedTextWriter.Indent = 1;
                        indentedTextWriter.WriteLine("[ Arguments ]");
                    }

                    foreach (PropertyInfo p in properties)
                    {
                        IEnumerable<KScriptProperty> Properties = p.GetCustomAttributes<KScriptProperty>(false);
                        foreach (KScriptProperty prop in Properties)
                        {
                            indentedTextWriter.Indent = 2;
                            indentedTextWriter.WriteLine("[" + ++index + "] - " + p.Name + (prop.IsRequired() ? " (required)" : ""));
                            indentedTextWriter.Indent = 3;
                            indentedTextWriter.WriteLine(prop.ToString());
                            IEnumerable<KScriptExample> Examples = p.GetCustomAttributes<KScriptExample>(false);
                            int example_count = 0;
                            if (Examples.Any())
                            {
                                indentedTextWriter.WriteLine("[ Examples ]");
                                foreach (KScriptExample example in Examples)
                                {
                                    indentedTextWriter.Indent = 4;
                                    indentedTextWriter.WriteLine(++example_count + " - " + example.ToString());
                                }
                            }

                            KScriptAcceptedOptions Accepted_Value = p.GetCustomAttribute<KScriptAcceptedOptions>(false);

                            if (Accepted_Value != null)
                            {
                                indentedTextWriter.Indent = 3;
                                indentedTextWriter.WriteLine("[ Accepted Values ]");

                                int val_count = 0;
                                foreach (string item in Accepted_Value.GetValues())
                                {
                                    indentedTextWriter.Indent = 4;
                                    indentedTextWriter.WriteLine(++val_count + " - " + item);
                                }
                            }
                        }
                    }

                    indentedTextWriter.WriteLine();
                }
            }

            indentedTextWriter.Dispose();

            Out();
            Out();
        }
    }
}