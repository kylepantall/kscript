using System;
using System.Runtime.InteropServices;
using KScript.Arguments;
using KScript.Handlers;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;

namespace KScript
{
    /// <summary>
    /// KScriptObject used for parsing of commands and arguments.
    /// </summary>
    [ClassInterface(ClassInterfaceType.None)]
    public abstract class KScriptObject : KScriptBaseObject
    {
        /// <summary>
        /// Initialises a KScriptObject with it's content as an object.
        /// </summary>
        /// <param name="Contents">Contents of KScript object.</param>
        public KScriptObject(object Contents) { }

        /// <summary>
        /// Initialises a KScriptObject with it's content as a string.
        /// </summary>
        /// <param name="Contents">Contents of KScript object.</param>
        public KScriptObject(string Contents) { }

        /// <summary>
        /// Initialises a KScriptObject without inner objects (no content).
        /// </summary>
        public KScriptObject() { }

        /// <summary>
        /// The content of the KScript object as a string.
        /// </summary>
        public string Contents { get; set; }

        /// <summary>
        /// The content of the KScript object as an object.
        /// </summary>
        public object ContentsAsObject { get; set; }

        /// <summary>
        /// Determines whether this KScriptObject should skip validation and should run whilst being parsed instead of after.
        /// </summary>
        public bool RunImmediately { get; set; } = false;

        /// <summary>
        /// Determines if this KScriptObject should be ignored completely.
        /// </summary>
        public bool Ignore { get; set; } = false;

        /// <summary>
        /// Used to handle any commands from a string such as Contents or KScriptObject properties.
        /// </summary>
        /// <param name="value">Value to handle commands from.</param>
        /// <returns>String with handled commands</returns>
        public string HandleCommands(string value) => KScriptCommandHandler.HandleCommands(ParentContainer.GetStringHandler().Format(value), ParentContainer);

        /// <summary>
        /// The script types
        /// </summary>
        public enum ScriptType
        {
            /// <summary>
            /// The script object contains several objects.
            /// </summary>
            ENUMERABLE,
            /// <summary>
            /// The script object is used to define properties or values.
            /// </summary>
            DEF,
            /// <summary>
            /// The script object is finite and does not contain several objects.
            /// </summary>
            OBJECT
        }

        /// <summary>
        /// Returns the type of script object.
        /// </summary>
        /// <returns>Script type</returns>
        public ScriptType GetScriptObjectType()
        {
            bool isEnumerable = typeof(KScriptConditional).IsAssignableFrom(GetType());
            return isEnumerable ? ScriptType.ENUMERABLE : (typeof(def).IsAssignableFrom(GetType()) ? ScriptType.DEF : ScriptType.OBJECT);
        }

        /// <summary>
        /// Method used to retrieve def objects with specified id.
        /// </summary>
        /// <param name="id">Id of object to retrieve.</param>
        /// <returns></returns>
        public def Def(string id)
        {
            string _id = id;
            if (id.StartsWith("$"))
            {
                _id = id.Substring(1);
            }

            return ParentContainer.GetDefs()[_id];
        }

        /// <summary>
        /// Used to return a property using the property name.
        /// </summary>
        /// <param name="propertyName">Name of property</param>
        /// <returns>The property</returns>
        public object this[string propertyName]
        {
            get
            {
                if (GetType().GetProperty(propertyName) != null)
                {
                    return GetType().GetProperty(propertyName).GetValue(this, null);
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if (GetType().GetProperty(propertyName) != null)
                {
                    GetType().GetProperty(propertyName).SetValue(this, value, null);
                }
                else
                {
                    Exception ex = new KScriptMissingAttribute(this);
                    Console.WriteLine(string.Format("KScriptObject '{0}' threw the error: {1}\nTry looking at the KScript help documentation.", GetType().Name, ex.Message));
                    Environment.Exit(0);
                }
            }
        }

        /// <summary>
        /// Used to initialise a KScriptObject
        /// </summary>
        /// <param name="container">The script container to attach the KScriptObject to.</param>
        public void Init(KScriptContainer container) => SetContainer(container);

        /// <summary>
        /// Validation code to ensure KScriptObject is correctly validated, throw KScriptInvalidScriptType or KScriptNoValidationNeeded
        /// </summary>
        abstract public void Validate();

        /// <summary>
        /// The KScriptObject method to run when this instance is instantiated
        /// </summary>
        /// <returns>true to continue, false to stop invoking this method</returns>
        abstract public bool Run();

        /// <summary>
        /// Used to offer detail on how to use this KScriptObject and implement it properly.
        /// </summary>
        /// <returns>Instructions as a string</returns>
        abstract public string UsageInformation();

    }
}
