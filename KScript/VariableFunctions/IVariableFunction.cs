using KScript.Arguments;
using KScript.KScriptObjects;
using System;

namespace KScript
{
    public abstract class IVariableFunction : KScriptBaseObject
    {
        /// <summary>
        /// Stores the variable id without the prefix ($)
        /// </summary>
        private readonly string Variable_ID;

        public IVariableFunction(KScriptContainer container, string variable_id) : base(container) => Variable_ID = variable_id;

        internal void Init(KScriptContainer container) => SetContainer(container);

        /// <summary>
        /// Used to retrieve the variable using the function.
        /// </summary>
        /// <returns></returns>
        public def GetDef()
        {
            return ParentContainer.GetDef(Variable_ID);
        }

        /// <summary>
        /// Evaluate the result based on the input of the given variable [value].
        /// </summary>
        /// <param name="value">The value of the variable</param>
        /// <returns>Evaluated string</returns>
        public abstract string Evaluate(params string[] args);

        /// <summary>
        /// Evaluates if this variable function can be executed on this type of value.
        /// </summary>
        /// <returns>If this type of variable is accepted.</returns>
        public abstract bool IsAccepted();
    }
}
