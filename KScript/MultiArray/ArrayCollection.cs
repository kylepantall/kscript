using System.Collections.Generic;

namespace KScript.MultiArray
{
    public class ArrayCollection : IArray
    {
        public List<IArray> Items;

        private bool _isBase = false;
        public List<IArray> GetItems() => Items;

        public IArray AtIndex(int index)
        {
            return GetItems()[index];
        }
        public bool IsBase() => _isBase;
        public ArrayCollection SetBase()
        {
            _isBase = true;
            return this;
        }
        public ArrayCollection UnsetBase()
        {
            _isBase = false;
            return this;
        }
        public void SetKey(string key) => Key = key;
        public void AddItem(IArray obj)
        {
            if (Items == null)
            {
                Items = new List<IArray>();
            }
            Items.Add(obj);
        }
        public ArrayCollection()
        {
            Items = new List<IArray>();
            _isBase = false;
        }
        public ArrayCollection(string key) : base() => Key = key;

        public ArrayCollection(bool isBase) : base() => SetBase();
        public ArrayCollection(List<IArray> items) => Items = items;

        public ArrayCollection(string key, List<IArray> items)
        {
            Key = key;
            Items = items;
        }
    }
}
