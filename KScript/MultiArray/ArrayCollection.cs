using System.Collections.Generic;

namespace KScript.MultiArray
{
    public class ArrayCollection : IArray
    {
        private readonly List<IArray> Items;
        public List<IArray> GetItems() => Items;
        public IArray AtIndex(int index)
        {
            var i = index - 1;
            return GetItems()[i];
        }

        public ArrayCollection() => Items = new List<IArray>();
        public ArrayCollection(List<IArray> items) => Items = items;

        public ArrayCollection(string key, List<IArray> items)
        {
            Key = key;
            Items = items;
        }
    }
}
