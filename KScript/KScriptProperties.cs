namespace KScript
{
    public class KScriptProperties
    {
        public bool Quiet { get; set; }
        public bool ClearAfterInput { get; set; }

        public KScriptProperties()
        {
            ClearAfterInput = false;
            Quiet = true;
        }
    }
}