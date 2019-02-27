using System;
using System.Collections.Generic;

namespace KScript.Handlers
{
    public class KScriptArrayContainer
    {
        public List<object> Array_Values { get; set; }

        public KScriptArrayContainer()
        {
            Array_Values = new List<object>();
        }

        public enum ValueType
        {
            KeyWithSingularValue,
            KeyWithMultipleValues,
            NoKeyWithMultipleValues,
            NoKeyWithSingularValue,
            Unknown
        }

        /// <summary>
        /// Determines the value type of the retrieved item from the multidimensional KScriptMArray.
        /// </summary>
        /// <param name="j"></param>
        /// <returns></returns>
        public ValueType GetObjectType(object j)
        {
            /* Possible values:
             *  1. KeyValuePair<string,string> - Key with Value.
             *  2. KeyValuePair<string,List<object>> - Key with multiple values
             *  3. List<object> - No key with multiple values.
             *  4. string - No key with singular value.
             */

            Type t = j.GetType();

            if (t.IsAssignableFrom(typeof(KeyValuePair<string, string>)))
            {
                return ValueType.KeyWithSingularValue;
            }

            if (t.IsAssignableFrom(typeof(KeyValuePair<string, List<object>>)))
            {
                return ValueType.KeyWithMultipleValues;
            }

            if (t.IsAssignableFrom(typeof(List<object>)))
            {
                return ValueType.NoKeyWithMultipleValues;
            }

            if (t.IsAssignableFrom(typeof(string)))
            {
                return ValueType.NoKeyWithSingularValue;
            }

            return ValueType.Unknown;
        }


        public void Add(object to_add_to, object value_to_add)
        {
            var type = GetObjectType(to_add_to);


            if (type == ValueType.KeyWithMultipleValues)
            {
                var obj = (KeyValuePair<string, List<object>>)to_add_to;
                obj.Value.Add(value_to_add);
            }

            if (type == ValueType.NoKeyWithMultipleValues)
            {
                var obj = (List<object>)to_add_to;
                obj.Add(value_to_add);
            }
        }

    }
}
