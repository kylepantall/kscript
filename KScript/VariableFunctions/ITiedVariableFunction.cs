using System;
using KScript.Arguments;
using KScript.KScriptObjects;

namespace KScript.VariableFunctions
{
    public abstract class ITiedVariableFunction : KScriptBaseObject
    {
        /// <summary>
        /// Stores the first variable id without the prefix ($)
        /// </summary>
        private readonly string First_VariableID;

        /// <summary>
        /// Stores the second variable id without the prefix ($)
        /// </summary>
        private readonly string Second_VariableID;

        public ITiedVariableFunction(KScriptContainer container, string first_variable_id, string second_variable_id) : base(container)
        {
            First_VariableID = first_variable_id;
            Second_VariableID = second_variable_id;
        }

        internal void Init(KScriptContainer container) => SetContainer(container);

        /// <summary>
        /// Used to retrieve the variable using the function.
        /// </summary>
        /// <returns></returns>
        public def GetFirstDef()
        {
            if (ParentContainer.GetDefs().ContainsKey(First_VariableID))
            {
                return ParentContainer.GetDefs()[First_VariableID];
            }
            else
            {
                Exception ex = new KScriptExceptions.KScriptDefNotFound(this, string.Format("The Def '{0}' could not be found.", First_VariableID));
                HandleException(ex);
                return null;
            }
        }

        /// <summary>
        /// Used to retrieve the second variable using the function.
        /// </summary>
        /// <returns></returns>
        public def GetSecondDef()
        {
            if (ParentContainer.GetDefs().ContainsKey(Second_VariableID))
            {
                return ParentContainer.GetDefs()[Second_VariableID];
            }
            else
            {
                Exception ex = new KScriptExceptions.KScriptDefNotFound(this, string.Format("The Def '{0}' could not be found.", Second_VariableID));
                HandleException(ex);
                return null;
            }
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