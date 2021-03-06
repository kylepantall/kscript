﻿namespace KScript.VariableFunctions
{
    class equal : ITiedVariableFunction
    {
        public equal(KScriptContainer container, string first_variable, string second_variable) : base(container, first_variable, second_variable) { }

        public override string Evaluate(params string[] args) => ToBoolString(GetFirstDef().Contents == GetSecondDef().Contents);

        public override bool IsAccepted() => true;
    }
}
