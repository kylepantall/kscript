namespace KScript.MultiArray
{
    public abstract class IArray
    {
        public string Key { get; set; }

        public bool Equals(string val) => Key.Equals(val, System.StringComparison.OrdinalIgnoreCase);

        public bool HasKey() => string.IsNullOrEmpty(Key);

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

        public IArray Find(string val)
        {
            IArray self = this;
            int valInt = -1;
            bool isIndex = int.TryParse(val, out valInt);

            if (IsCollection())
            {
                if (isIndex)
                {
                    return GetCollection().AtIndex(valInt);
                }
                else
                {
                    if (Equals(val))
                        return this;
                    else
                    {
                        foreach (IArray item in GetCollection().GetItems())
                            return item.Find(val);
                    }
                }
            }
            else
            {
                if (!isIndex)
                {
                    if (Equals(val))
                        return this;
                }
                else return this;
            }

            return null;
        }

        public IArray FindWithKey(string val)
        {
            if (IsCollection())
                return GetCollection().Find(val);
            else
            {
                if (Equals(val))
                    return this;
                else
                    return null;
            }
        }

    }
}
