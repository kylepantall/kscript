using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace KScript.MultiArray
{
    public class ArrayBase
    {
        private readonly ArrayCollection Root;
        public ArrayBase(ArrayCollection Root) => this.Root = Root;
        public ArrayCollection GetRoot() => Root;
        public IArray Find(Match[] values)
        {
            IArray current_node = GetRoot().HasKey() ? new ArrayCollection(
                                new List<IArray>() {
                                     GetRoot()
                                }) : GetRoot();

            foreach (var item in values)
            {
                if (current_node == null)
                {
                    continue;
                }

                if (int.TryParse(item.Groups[1].Value, out int index))
                {
                    current_node = current_node.Find(index);
                    continue;
                }
                current_node = current_node.Find(item.Groups[1].Value);
            }

            return current_node;
        }
    }
}
