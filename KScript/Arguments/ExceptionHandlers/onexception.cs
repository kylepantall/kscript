using KScript.Metadata;
namespace KScript.Arguments
{
    [KScriptRetired("Use <try> with <catch as=\"err\">.")]
    public class onexception : KScriptExceptionWrapper
    {
        public override bool Run() => true;
    }
}
