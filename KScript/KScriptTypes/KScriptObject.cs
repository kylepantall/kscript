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
        public KScriptObject(object contents) { }
        public KScriptObject(String contents) { }
        public KScriptObject() { }

        public enum ScriptType
        {
            ENUMERABLE,
            DEF,
            OBJECT
        }

        public ScriptType GetScriptObjectType()
        {
            bool isEnumerable = typeof(KScriptObjectEnumerable).IsAssignableFrom(GetType());
            return isEnumerable ? ScriptType.ENUMERABLE : (typeof(def).IsAssignableFrom(GetType()) ? ScriptType.DEF : ScriptType.OBJECT);
        }

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

        public void Init(KScriptContainer container)
        {
            SetContainer(container);
        }

        abstract public void Validate();
        abstract public void Run();
    }
}
