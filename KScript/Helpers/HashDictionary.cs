using System.Linq;
using System.Text;
using System;
using System.Collections.Generic;

namespace KScript
{
    public class HashDictionary<TKey, XValue>
        where TKey : class
        where XValue : struct
    {
        private Dictionary<TKey, HashSet<XValue>> @initiatedObject;
        public HashDictionary() => initiatedObject = new Dictionary<TKey, HashSet<XValue>>();

        public new string ToString()
        {
            StringBuilder builder = new StringBuilder();

            var longest = Keys().Select(key => key.ToString()).OrderByDescending(key => key.Length).FirstOrDefault().Length;
            var divide = new string('-', longest);

            builder.AppendLine(divide);
            builder.AppendLine(string.Format(" {0,-10} | {1,-10} ", "Value", "Timestamp"));
            builder.AppendLine(divide);

            foreach (var key in Keys())
            {
                foreach (var value in @initiatedObject[key])
                {
                    var keyValue = value.Equals(@initiatedObject[key].First()) ? key.ToString() : EmptyEqualLength(key);
                    builder.AppendLine(String.Format(" {0,-10} | {1,-10} ", keyValue, value.ToString()));
                }

                builder.AppendLine("---------------------------------------");
            }

            return builder.ToString();
        }

        private string EmptyEqualLength(object value)
        {
            var str = value.ToString();
            value.ToString().ToCharArray().ToList().ForEach(item => str = str.Replace(item, ' '));
            return str;
        }


        public string FindUsingValue(Func<XValue, bool> value)
        {
            foreach (var item in @initiatedObject)
            {
                foreach (var child in item.Value)
                {
                    if (value.Invoke(child))
                    {
                        return item.Key.ToString();
                    }
                }
            }

            return Global.Values.NULL;
        }

        private List<TKey> Keys()
        {
            return @initiatedObject.Keys.ToList();
        }

        private bool HasSecondaryObject(TKey key)
        {
            bool hasKey = @initiatedObject.ContainsKey(key);

            if (!hasKey)
            {
                return false;
            }

            return @initiatedObject[key] != null;
        }

        public bool HasKey(TKey key, bool expect = true) => expect == @initiatedObject.ContainsKey(key);

        public HashDictionary<TKey, XValue> HasKey(TKey key, Action<HashDictionary<TKey, XValue>> func, bool expect = true)
        {
            if (HasKey(key) == expect)
            {
                func.Invoke(this);
                return this;
            }

            return this;
        }

        public HashDictionary<TKey, XValue> IfNotContain(TKey key, Action<HashDictionary<TKey, XValue>> func)
        {
            if (!HasKey(key))
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
                    if (!@initiatedObject[key].ToList().Any((x) =>
                    {
                        return x.Equals(value);
                    }))
                    {
                        @initiatedObject[key].Add(value);
                    }
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