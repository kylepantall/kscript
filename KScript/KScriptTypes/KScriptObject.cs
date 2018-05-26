using KScript.Arguments;
using KScript.KScriptTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KScript
{
    public abstract class KScriptObject : KScriptIO
    {
        public KScriptObject(object contents) : this() { }
        public KScriptObject(String contents) : this() { }
        public KScriptObject() : base() { }

        private String XAML = "";

        public def Def(string id)
        {
            string _id = id;
            if (id.StartsWith("$")) _id = id.Substring(1);
            return ParentContainer.defs[_id];
        }

        public object this[string propertyName]
        {
            get { if (GetType().GetProperty(propertyName) != null) return GetType().GetProperty(propertyName).GetValue(this, null); else return null; }
            set { GetType().GetProperty(propertyName).SetValue(this, value, null); }
        }

        public void Init(KScriptContainer container, string code)
        {
            SetContainer(container);
            XAML = code;
        }

        abstract public void Validate();
        abstract public void Run();
    }
}
