using System.Runtime.InteropServices;

namespace KScript
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptProperties
    {
        public bool Quiet { get; set; }
        public bool ClearAfterInput { get; set; }
        public bool WaitOnFinish { get; set; }
        public bool FullScreen { get; set; }
        public string Language { get; set; }
        public bool DynamicDefs {get; set;}

        public KScriptProperties()
        {
            ClearAfterInput = false;
            Quiet = true;
            FullScreen = false;
            WaitOnFinish = true;
            DynamicDefs = true;
        }
    }
}