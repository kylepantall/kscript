using KScript.Handlers;
using KScript.KScriptObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace KScript.CommandHandler
{
    [ClassInterface(ClassInterfaceType.None)]
    public class ICommandObject : ICommand
    {
        private readonly KScriptContainer kScriptContainer;
        private readonly KScriptBaseObject kScriptBaseObject;

        public ICommandObject(string value, KScriptContainer container, KScriptBaseObject parent) : base(value)
        {
            kScriptContainer = container;
            kScriptBaseObject = parent;
        }

        public int EndNameIndex { get; set; } = -1;

        public string CommandParameters => Value.Substring(IndexProperties.Start, IndexProperties.End + 1 - IndexProperties.Start);
        public string Command => Value.Substring(IndexProperties.Start + 1, (EndNameIndex - IndexProperties.Start) - 1);
        public string InnerCommand => Value.Substring(EndNameIndex + 1, (IndexProperties.End - EndNameIndex) - 1);

        public bool HasChildren => Children.Any();

        public Queue<ICommand> Children { get; set; } = new Queue<ICommand>();

        public bool IsOuter(int start_x, int end_x)
        {
            bool a = start_x > IndexProperties.Start && start_x < IndexProperties.End;
            bool b = end_x < IndexProperties.End && end_x > IndexProperties.Start;
            return a && b;
        }


        public override string CalculateValue()
        {
            if (HasChildren)
            {
                string[] @params = Children.Select(i => i.CalculateValue()).ToArray();
                string type_name = Command.ToLower();
                Type _type = KScriptCommandHandler.GetCommandType(type_name);
                KScriptCommand cmd = KScriptCommandHandler.GetCommandObject(@params.ToArray(), _type, kScriptContainer, kScriptBaseObject);
                return cmd.Calculate();
            }
            else
            {
                string[] @params = Children.Select(i => i.CalculateValue()).ToArray();
                string type_name = Command.ToLower();
                Type _type = KScriptCommandHandler.GetCommandType(type_name);
                KScriptCommand cmd = KScriptCommandHandler.GetCommandObject(_type, kScriptContainer, kScriptBaseObject);
                return cmd.Calculate();
            }
        }
    }
}
