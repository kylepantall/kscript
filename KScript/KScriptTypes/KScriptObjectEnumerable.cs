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
        public bool Ignore { get; set; } = false;

        public KScriptObjectEnumerable() => Children = new List<KScriptObject>();

        public List<KScriptObject> Children { get; set; }
        public override void Run()
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
        }

        public override void Validate()
        {
            throw new KScriptNoValidationNeeded();
        }
    }
}
