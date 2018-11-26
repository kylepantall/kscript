using KScript.KScriptTypes;

namespace KScript.KScriptObjects
{
    public class KScriptBaseObject : KScriptIO
    {
        public KScriptBaseObject() : base() { }
        public KScriptBaseObject(KScriptContainer container) : base(container) { }
    }
}
