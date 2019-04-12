namespace KScript.MultiArray
{
    public class ArrayItem : IArray
    {
        public string Value { get; set; }

        public ArrayItem(string value) => Value = value;

        public ArrayItem(string key, string value)
        {
            Key = key;
            Value = value;
        }

        //public object FindKey(string key)
        //{
        //    if (Value.GetType().IsAssignableFrom(typeof(List<ArrayItem>)))
        //    {
        //        List<ArrayItem> items = (List<ArrayItem>)Value;
        //        if (key.StartsWith("~"))
        //        {
        //            return items.Select(i => i.FindKey(key)).FirstOrDefault();
        //        }
        //        else
        //        {
        //            ArrayItem found_item = items.FirstOrDefault(x => x.Key == key);
        //            return found_item;
        //        }
        //    }

        //    return null;
        //}
    }
}
