using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript
{
    public class KScriptObjectEnumerable : KScriptObject
    {
        public KScriptObjectEnumerable() => Children = new List<KScriptObject>();

        public List<KScriptObject> Children { get; set; }
        public override bool Run()
        {
            if (!Ignore)
            {
                foreach (var item in Children)
                {
                    if (item != null)
                    {
                        item.Init(ParentContainer);
                        item.Validate();
                        item.Run();
                    }
                }
            }
            return true;
        }

        public override void Validate() => throw new KScriptNoValidationNeeded();
        public override string UsageInformation() => @"Used to store an array of KScriptObjects.";
    }
}
