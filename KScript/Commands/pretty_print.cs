using KScript.MultiArray;
using System;
using System.CodeDom.Compiler;
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
            //ArrayBase container = ParentContainer.GetMultidimensionalArrays()[array_id];

            //if (container != null)
            //{

            //    ArrayCollection root = container.GetRoot();
            //    IArray prev = container.GetRoot();
            //    IndentedTextWriter builder = new IndentedTextWriter(Console.Out);
            //    int indent = 0;

            //    if (root != null)
            //    {
            //        if (root.HasChildren())
            //        {
            //            Iterate(root.GetItems(), out indent);
            //        }
            //        else
            //        {
            //            //Do nothing
            //        }
            //    }

            //}

            return "[INFO] In Development.";
        }

        public IArray Iterate(List<IArray> current, out int indent)
        {
            indent = 1;
            return null;
        }

        public override void Validate()
        {

        }
    }
}
