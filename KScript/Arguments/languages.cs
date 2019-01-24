namespace KScript.Arguments
{
    public class languages : KScriptObject
    {
        public override bool Run() => true;

        public override string UsageInformation() => "Used to define language dependent variables.";

        public override void Validate()
        {

        }
    }
}
