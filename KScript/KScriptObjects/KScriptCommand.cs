using KScript.KScriptObjects;

namespace KScript
{
    public abstract class KScriptCommand : KScriptBaseObject
    {
        public KScriptCommand(KScriptContainer container, KScriptObject parent) : base(container) { }
        public KScriptCommand() { }

        internal void Init(KScriptContainer container, KScriptBaseObject parent)
        {
            SetContainer(container);
            SetBaseScriptObject(parent);
        }

        public bool IsEmpty(string value) => string.IsNullOrEmpty(value);

        public abstract string Calculate();
        public abstract void Validate();
    }
}
