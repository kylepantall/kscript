using System;
using System.Runtime.InteropServices;
using KScript.Handlers;
using KScript.KScriptObjects;

namespace KScript.KScriptTypes
{
    [ClassInterface(ClassInterfaceType.None)]
    public class KScriptIO
    {
        private KScriptBaseObject ScriptObject;

        /// <summary>
        /// Constructor for KScriptIO class.
        /// </summary>
        public KScriptIO() { }


        /// <summary>
        /// Sets the Base script object to obj.
        /// </summary>
        /// <param name="obj">Base script object</param>
        public void SetBaseScriptObject(KScriptBaseObject obj) => ScriptObject = obj;


        /// <summary>
        /// Returns the base script object.
        /// </summary>
        /// <returns>Base Script object</returns>
        public KScriptBaseObject GetBaseObject() => ScriptObject;


        /// <summary>
        /// Constructor with KScriptContainer object initialised.
        /// </summary>
        /// <param name="container">The KScriptContainer to initialise with.</param>
        public KScriptIO(KScriptContainer container) => SetContainer(container);

        /// <summary>
        /// ParentContainer property used to store the KScriptContainer
        /// </summary>

        internal KScriptContainer ParentContainer { get; private set; }


        internal void HandleException(Exception ex, KScriptObject obj = null)
        {
            if (ParentContainer != null)
            {
                if (obj != null)
                {
                    ParentContainer.HandleException(obj, ex);
                }
                else
                {
                    ParentContainer.HandleException(ex);
                }
            }
        }

        internal void HandleException(Exception ex, KScriptCommand obj)
        {
            if (ParentContainer != null)
            {
                ParentContainer.HandleException(obj, ex);
            }
        }


        /// <summary>
        /// Method used to set the ParentContainer property.
        /// </summary>
        /// <param name="container">The KScriptContainer object to use.</param>
        internal void SetContainer(KScriptContainer container) => ParentContainer = container;

        /// <summary>
        /// Add a type to the LoadedKScriptObjects array using specified type.
        /// </summary>
        /// <param name="val">The type to add to the types array.</param>
        internal void AddType(Type val) => ParentContainer.LoadedKScriptObjects.Add(val.Name.ToLower(), val);

        /// <summary>
        /// Method used to retrieve the ParentContainer property.
        /// </summary>
        /// <returns>Returns the value of ParentContainer.</returns>
        protected KScriptContainer KScript() => ParentContainer;

        /// <summary>
        /// Method to return string input to a bool.
        /// </summary>
        /// <param name="in">The string to convert to a bool.</param>
        /// <returns>Bool value.</returns>
        public bool ToBool(string @in) => KScriptBoolHandler.Convert(@in);

        /// <summary>
        /// Method use to convert bool to string appropriate for KScript parsing.
        /// </summary>
        /// <param name="in">Bool value to convert.</param>
        /// <returns>String equivalent for KScript bool.</returns>
        public string ToBoolString(bool @in) => @in ? "yes" : "no";


        /// <summary>
        /// For null properties and values
        /// </summary>
        /// <returns>NULL</returns>
        public string NULL => Global.Values.NULL;


        public void Out() => Console.Out.WriteLine();
        public void Out(string val) => Console.Out.Write(KScriptCommandHandler.HandleCommands(ParentContainer.GetStringHandler().Format(val), ParentContainer, GetBaseObject()));
        public void Out(object obj) => Out(obj.ToString());

        public string Format(string val) => ParentContainer.GetStringHandler().Format(val);

        public string ReturnFormattedVariables(string val) => KScriptVariableHandler.ReturnFormattedVariables(ParentContainer, val);

        public string In() => Console.In.ReadLine();
        public string In(string prompt) { Out(prompt); return Console.In.ReadLine(); }

        public int InNumber() => int.Parse(Console.ReadLine().Trim());
        public int InNumber(string prompt) { Out(prompt); return int.Parse(Console.ReadLine().Trim()); }

        public string Tab(int tabs)
        {
            string tmp = "";
            for (int i = 0; i < tabs; i++) { tmp += string.Format(@"\t"); };
            return tmp;
        }
    }
}
