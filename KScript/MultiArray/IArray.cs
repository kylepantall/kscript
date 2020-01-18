namespace KScript.MultiArray
{
    public abstract class IArray
    {
        public string Key { get; set; }

        public bool Equals(string val)
        {
            if (HasKey())
            {
                if (Key.Length > 0)
                {
                    return Key.Equals(val, System.StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
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
                return string.Empty;
            }
            else
            {
                return GetArrayItem().Value;
            }
        }

        public bool HasChildren() => IsCollection() ? GetCollection().GetItems().Count > 0 : false;

        public IArray Find(string key)
        {
            if (IsCollection())
            {
                foreach (IArray item in GetCollection().GetItems())
                {
                    if (item.Equals(MultiArrayParser.StripKey(key)))
                    {
                        return item;
                    }
                }
            }
            else
            {
                if (GetArrayItem().Equals(MultiArrayParser.StripKey(key)))
                {
                    return GetArrayItem();
                }
            }
            return null;
        }

        public IArray Find(int index)
        {
            if (IsCollection())
            {
                System.Collections.Generic.List<IArray> x = GetCollection().GetItems();

                if (index > x.Count)
                {
                    throw new KScriptExceptions.KScriptIndexOutOfBoundsException(null);
                }
                else
                {
                    return x[index];
                }
            }

            return null;
        }

    }
}
