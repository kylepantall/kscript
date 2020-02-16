using KScript.KScriptExceptions;
using System;
using System.Collections.Generic;

namespace KScript.Commands
{
    public class acount : KScriptCommand
    {
        private readonly string id;
        public acount(string id) => this.id = id;
        public override string Calculate()
        {
            if (id.StartsWith("~") && ParentContainer.GetMultidimensionalArrays().ContainsKey(MultiArray.MultiArrayParser.StripKey(id)))
            {
                var arrayItem = MultiArray.MultiArrayParser.GetArrayItem(id, ParentContainer);

                if (arrayItem is null)
                {
                    string arrayKey = MultiArray.MultiArrayParser.StripKey(id);
                    return ParentContainer.GetMultidimensionalArrays()[arrayKey].GetRoot().Count().ToString();
                }

                return arrayItem.GetCollection().GetItems().Count.ToString();
            }

            List<string> array = KScript().ArrayGet(this, id);

            if (array is null)
            {
                throw new KScriptArrayNotFound(this);
            }

            return array.Count.ToString();
        }

        public override void Validate() { }
    }
}
