namespace KScript.Document
{
    public interface IKScriptDocumentNode
    {
        void Run(KScriptContainer container, string args);
        bool Ignore { get; set; }
        KScriptObject GetValue();
    }
}
