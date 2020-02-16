namespace KScript.MultiArray
{
    public abstract class IArray
    {
        public string Key { get; set; }

        public bool Equals(string val)
        {
            if (!HasKey())
            {
                return false;
            }

            return Key.Length > 0 ? Key.Equals(val, System.StringComparison.OrdinalIgnoreCase) : false;
        }

        public bool HasKey() => !string.IsNullOrEmpty(Key);

        public bool IsCollection() => GetType().IsAssignableFrom(typeof(ArrayCollection));

        public bool IsArrayItem() => GetType().IsAssignableFrom(typeof(ArrayItem));

        public ArrayCollection GetCollection() => (ArrayCollection)this;
        public ArrayItem GetArrayItem() => (ArrayItem)this;

        public string GetValue()
        {
            if (IsCollection())
            {
                return null;
            }

            return GetArrayItem().Value;
        }

        public bool HasChildren() => IsCollection() ? GetCollection().Count() > 0 : false;

        public IArray Find(string key)
        {
            if (IsCollection())
            {
                foreach (var item in GetCollection().GetItems())
                {
                    if (!item.Equals(MultiArrayParser.StripKey(key)))
                    {
                        continue;
                    }

                    return item;
                }
            }

            if (!GetArrayItem().Equals(MultiArrayParser.StripKey(key)))
            {
                return null;
            }

            return GetArrayItem();
        }

        public IArray Find(int index)
        {
            if (!IsCollection())
            {
                return null;
            }

            var collectionItems = GetCollection().GetItems();

            if (index <= collectionItems.Count && index >= 0)
            {
                return collectionItems[index];
            }

            throw new KScriptExceptions.KScriptIndexOutOfBoundsException(null);
        }

    }
}
