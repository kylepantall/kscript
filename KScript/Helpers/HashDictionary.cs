using System.Linq.Expressions;
using System;
using System.Collections.Generic;
namespace KScript
{
    public class HashDictionary<TKey, XValue>
        where TKey : class
        where XValue : new()
    {
        private Dictionary<TKey, HashSet<XValue>> @initiatedObject;
        public HashDictionary() => initiatedObject = new Dictionary<TKey, HashSet<XValue>>();


        private bool HasSecondaryObject(TKey key)
        {
            bool hasKey = @initiatedObject.ContainsKey(key);

            if (!hasKey)
            {
                return false;
            }

            return @initiatedObject[key] != null;
        }

        public bool HasKey(TKey key) => @initiatedObject.ContainsKey(key);

        public HashDictionary<TKey, XValue> HasKey(TKey key, Action<HashDictionary<TKey, XValue>> func)
        {
            if (HasKey(key))
            {
                func.Invoke(this);
                return this;
            }

            return this;
        }

        public bool Insert(TKey key, XValue value)
        {
            try
            {
                if (HasSecondaryObject(key))
                {
                    @initiatedObject[key].Add(value);
                }

                HashSet<XValue> _ = new HashSet<XValue>();
                _.Add(value);
                @initiatedObject.Add(key, _);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public XValue Pop(TKey key, XValue value)
        {
            CanProceed(key);
            @initiatedObject[key].Remove(value);
            return value;
        }


        private void CanProceed(TKey key)
        {
            if (!HasSecondaryObject(key))
            {
                throw new Exception("There's no key or secondary object instantiated.");
            }
        }

        public bool Contains(TKey key, XValue value)
        {
            CanProceed(key);

            return @initiatedObject[key].Contains(value);
        }
    }
}