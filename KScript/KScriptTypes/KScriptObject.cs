using KScript.Arguments;
using KScript.Handlers;
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

        public bool Ignore { get; set; } = false;

        public string HandleCommands(string value) => KScriptCommandHandler.HandleCommands(ParentContainer.StringHandler.Format(value), ParentContainer);

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

        /// <summary>
        /// Validation code to ensure KScriptObject is correctly validated, throw KScriptInvalidScriptType or KScriptNoValidationNeeded
        /// </summary>
        abstract public void Validate();

        /// <summary>
        /// The KScriptObject method to run when this instance is instantiated
        /// </summary>
        /// <returns>true to continue, false to stop invoking this method</returns>
        abstract public bool Run();

        /// <summary>
        /// Used to offer detail on how to use this KScriptObject and implement it properly.
        /// </summary>
        /// <returns>Instructions as a string</returns>
        abstract public string UsageInformation();
    }
}
