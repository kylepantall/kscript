namespace KScript.Document
{
    public interface IKScriptDocumentNode
    {
        void Run(KScriptContainer container, string args, KScriptObject obj);
        bool Ignore { get; set; }
        KScriptObject GetValue();
    }
}
