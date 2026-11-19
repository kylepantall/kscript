using System.Runtime.InteropServices;

namespace KScript
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptProperties
    {
        public bool Quiet { get; set; }

        /// <summary>
        /// Set by the host (e.g. the -s CLI flag) to keep the script quiet regardless of the script's own quiet attribute.
        /// </summary>
        public bool ForceQuiet { get; set; }
        public bool ClearAfterInput { get; set; }
        public bool WaitOnFinish { get; set; }
        public bool FullScreen { get; set; }
        public string Language { get; set; }
        public bool DynamicDefs { get; set; }
        public bool PrintInfo { get; set; }

        public bool ThrowAllExceptions { get; set; }

        public KScriptProperties()
        {
            ClearAfterInput = false;
            Quiet = true;
            FullScreen = false;
            WaitOnFinish = true;
            DynamicDefs = false;
            ThrowAllExceptions = false;
            PrintInfo = false;
        }
    }
}