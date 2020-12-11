namespace KScript.CommandHandler
{
    public abstract class ICommand
    {

        public ICommand(string value) => Value = value;

        public Indexes IndexProperties { get; set; } = new Indexes();
        public int Index { get; set; } = 0;
        public string Value { get; set; } = "";

        public bool IsClosed => IndexProperties.End > -1;

        public abstract string CalculateValue();

        public bool IsCommandObject => GetType().IsAssignableFrom(typeof(ICommandObject));

        public ICommandObject GetCommandObject() => (ICommandObject)this;
        public IValue GetValueObject() => (IValue)this;

        public class Indexes
        {
            public int Start { get; set; } = -1;
            public int End { get; set; } = -1;
        }


        public override string ToString()
        {
            if (IsCommandObject)
            {
                return GetCommandObject().Command;
            }
            
            return Value;
        }
    }
}
