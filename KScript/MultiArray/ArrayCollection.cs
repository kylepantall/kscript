using System.Collections.Generic;

namespace KScript.MultiArray
{
    public class ArrayCollection : IArray
    {
        private readonly List<IArray> Items;
        public List<IArray> GetItems() => Items;
        public IArray AtIndex(int index)
        {
            return GetItems()[index];
        }


        public void AddItem(IArray obj) => Items.Add(obj);

        public ArrayCollection() => Items = new List<IArray>();
        public ArrayCollection(List<IArray> items) => Items = items;

        public ArrayCollection(string key, List<IArray> items)
        {
            Key = key;
            Items = items;
        }
    }
}
