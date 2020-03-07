using System.Linq;
using System;
using System.CodeDom.Compiler;
using KScript.MultiArray;
using System.Collections.Generic;

namespace KScript.Commands
{
    public class pretty_print : KScriptCommand
    {
        public string array_id { get; set; }

        public pretty_print(string array_id)
        {
            this.array_id = array_id;
        }

        public override string Calculate()
        {
            try
            {
                int indent = 0;

                System.IO.StringWriter baseTextWriter = new System.IO.StringWriter();
                IndentedTextWriter builder = new IndentedTextWriter(baseTextWriter);
                string strippedKey = MultiArray.MultiArrayParser.StripKey(array_id);
                IArray arrayItem = MultiArray.MultiArrayParser.GetArrayItem(array_id, KScript());

                if (arrayItem is null)
                {
                    Iterate(KScript().GetMultidimensionalArrays()[strippedKey].GetRoot(), 0, builder, indent, true);
                    return baseTextWriter.ToString();
                }

                Iterate(arrayItem, 0, builder, indent, true);
                return baseTextWriter.ToString();
            }
            catch (System.Exception ex)
            {
                HandleException(ex);
                return ex.Message;
            }
        }

        private string GetKey(string key, int index)
        {
            if (IsEmpty(key))
            {
                return $"{index}";
            }

            return $"\"{key}\"";
        }

        public void Iterate(IArray item, int index, IndentedTextWriter writer, int indentation = 0, bool LastItem = false)
        {
            writer.Indent = indentation;
            if (item.HasChildren())
            {
                writer.WriteLine($"[{GetKey(item.Key, index)}] => {{");
                indentation++;

                var collection = item.GetCollection().GetItems();
                var lastItem = collection.Last();

                foreach (var child in collection)
                {
                    bool lastChild = lastItem.Equals(child);
                    Iterate(child,
                            collection.IndexOf(child),
                            writer,
                            indentation,
                            lastChild);
                }

                indentation--;
                writer.Indent = indentation;
                writer.WriteLine($"}}{(LastItem ? "" : ",")}");
                return;
            }

            writer.WriteLine($"[{GetKey(item.Key, index)}] => \"{item.GetValue()}\"{(LastItem ? "" : ",")}");
        }

        public override void Validate()
        {

        }
    }
}
