namespace KScript.Runtime
{
    /// <summary>A base value with the instance methods of its declared refinement.</summary>
    public sealed class RefinedValue
    {
        internal RefinedValue(RefinedType type, object value)
        {
            Type = type;
            Value = Values.Unwrap(value);
        }

        public RefinedType Type { get; }
        public object Value { get; }

        public override string ToString() => Values.ToStr(Value);
    }
}
