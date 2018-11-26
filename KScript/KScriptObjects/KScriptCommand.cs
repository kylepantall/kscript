using KScript.KScriptObjects;

namespace KScript
{
    public abstract class KScriptCommand : KScriptBaseObject
    {
        public KScriptCommand(KScriptContainer container) : base(container) { }
        public KScriptCommand() { }
        internal void Init(KScriptContainer container) => SetContainer(container);
        public abstract string Calculate();
    }
}
