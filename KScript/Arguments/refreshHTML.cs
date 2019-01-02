using KScript.KScriptContainerObjects;

namespace KScript.Arguments
{
    public class refreshHTML : KScriptObject
    {
        public string to { get; set; }

        public override bool Run()
        {
            KScriptHtmlContainerObject obj = ParentContainer.GetObjectStorageContainer().GetObjectFromUID<KScriptHtmlContainerObject>(to);
            return true;
        }

        public override string UsageInformation()
        {
            return "Used to update content within a loadHtml KScriptObject.";
        }

        public override void Validate()
        {
            throw new KScriptExceptions.KScriptNoValidationNeeded(this);
        }
    }
}
