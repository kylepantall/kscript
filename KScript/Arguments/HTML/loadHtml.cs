using KScript.Metadata;
using KScript.KScriptContainerObjects;

namespace KScript.Arguments
{
    [KScriptRetired("Use kscript serve with HTML pages.")]
    public class loadHtml : KScriptHtmlContainerObject
    {
        public loadHtml(string contents) : base(contents) { }
    }
}
