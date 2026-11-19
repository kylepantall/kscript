using KScript.Metadata;
using System;

namespace KScript.Commands
{
    [KScriptRetired("Not part of KScript 2.")]
    public class parent : KScriptCommand
    {
        private readonly string property;

        public parent(string property) => this.property = property;

        public override string Calculate()
        {
            return GetBaseObject().GetPropertyValue(property);
        }


        public override void Validate() { }
    }
}
