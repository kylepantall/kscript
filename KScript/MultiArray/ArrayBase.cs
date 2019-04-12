using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace KScript.MultiArray
{
    public class ArrayBase
    {
        private readonly ArrayCollection Root;
        public ArrayBase(ArrayCollection r) => Root = r;
        public ArrayCollection GetRoot() => Root;

        public IArray Find(Match[] values)
        {
            IArray current_node = new ArrayCollection(new List<IArray>() { GetRoot() });
            int index = -1;

            foreach (var item in values)
            {
                if (int.TryParse(item.Groups[1].Value, out index))
                {
                    if (current_node != null)
                        current_node = current_node.Find(index);
                }
                else
                {
                    if (current_node != null)
                        current_node = current_node.Find(item.Groups[1].Value);
                }
            }

            if (current_node != null)
            {
                return current_node;
            }
            else
            {
                return null;
            }
        }
    }
}
