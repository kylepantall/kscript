using KScript.KScriptTypes;

namespace KScript
{
    public abstract class KScriptCommand : KScriptIO
    {
        public KScriptCommand(KScriptContainer container) : base(container) { }
        public KScriptCommand() { }
        internal void Init(KScriptContainer container) => SetContainer(container);
        public abstract string Calculate();
    }
}
