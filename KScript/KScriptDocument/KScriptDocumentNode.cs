namespace KScript.Document
{
    public class KScriptDocumentNode : IKScriptDocumentNode
    {
        private KScriptObject Value;
        public KScriptObject GetValue() => Value;

        public bool Ignore { get; set; } = false;
        public void Run()
        {
            if (!Ignore)
                try { GetValue().Run(); }
                catch (KScriptTypes.KScriptExceptions.KScriptSkipScriptObject) { }
        }

        public KScriptDocumentNode(KScriptObject obj) => Value = obj;
    }
}
