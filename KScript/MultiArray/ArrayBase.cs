namespace KScript.MultiArray
{
    public class ArrayBase
    {
        private readonly ArrayCollection Root;
        public ArrayBase(ArrayCollection r) => Root = r;
        public ArrayCollection GetRoot() => Root;
    }
}
