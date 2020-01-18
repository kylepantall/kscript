using KScript.Arguments;
using KScript.Handlers;
using KScript.KScriptExceptions;
using KScript.KScriptObjects;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace KScript
{
    /// <summary>
    /// KScriptObject used for parsing of commands and arguments.
    /// </summary>
    [ClassInterface(ClassInterfaceType.None)]
    public abstract class KScriptObject : KScriptBaseObject
    {
        private readonly List<string> Tags;

        /// <summary>
        /// Returns collection of KScriptObject tags.
        /// </summary>
        /// <returns>Returns the collection of tags</returns>
        protected List<string> GetTags() => Tags;

        /// <summary>
        /// Adds a tag for this KScriptObject.
        /// </summary>
        /// <param name="id">ID to use</param>
        /// <param name="value">Value to tag with</param>
        protected void AddTag(string value) => Tags.Add(value);


        /// <summary>
        /// Returns if the given KScriptObject has the given tag.
        /// </summary>
        /// <param name="tag">Tag to search this object for.</param>
        /// <returns>If the tag is attached.</returns>
        protected bool HasTag(string tag) => Tags.Contains(tag);


        public enum ValidationTypes
        {
            /// <summary>
            /// Defines that validation should occur before parsing this KScript Object.
            /// </summary>
            BEFORE_PARSING,

            /// <summary>
            /// Defines that validation should occur as this KScript Object is parsed.
            /// </summary>
            DURING_PARSING,

            /// <summary>
            /// Defines that validation should occur before and during the parsing of this KScriptObject.
            /// </summary>
            BOTH
        }

        /// <summary>
        /// Sets the KScriptObject to be ignored.
        /// </summary>
        public void SetIgnore() => Ignore = true;

        /// <summary>
        /// Defines when validation should occur.
        /// </summary>
        public ValidationTypes ValidationType { get; set; } = ValidationTypes.BEFORE_PARSING;

        /// <summary>
        /// Initialises a KScriptObject with it's content as an object.
        /// </summary>
        /// <param name="Contents">Contents of KScript object.</param>
        public KScriptObject(object Contents) : this() => ContentsAsObject = Contents;

        /// <summary>
        /// Initialises a KScriptObject with it's content as a string.
        /// </summary>
        /// <param name="Contents">Contents of KScript object.</param>
        public KScriptObject(string Contents) : this() => this.Contents = Contents;


        /// <summary>
        /// Sets the parent KScriptObject to current object.
        /// </summary>
        public KScriptObject()
        {
            SetBaseScriptObject(this);
            Tags = new List<string>();
        }

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
        public string HandleCommands(string value) => KScriptCommandHandler.HandleCommands(ParentContainer.GetStringHandler().Format(value), ParentContainer, this);

        /// <summary>
        /// Method used to handle commands without converting variables so variables are handled by the commands directly.
        /// </summary>
        /// <param name="value">String to parse.</param>
        /// <returns>Handled commands from raw input</returns>
        public string HandleRawCommands(string value) => KScriptCommandHandler.HandleCommands(value, ParentContainer, this);

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
                return null;
            }
            set
            {
                if (GetType().GetProperty(propertyName) != null)
                {
                    GetType().GetProperty(propertyName).SetValue(this, value, null);
                }
                else
                {
                    Exception ex = new KScriptMissingAttribute(this, string.Format("KScript Property '{0}' could not be found.", propertyName));
                    Console.WriteLine(string.Format("KScriptObject '{0}' threw the error: {1}\nTry looking at the KScript help documentation.", GetType().Name, ex.Message));
                }
            }
        }

        /// <summary>
        /// Used to initialise a KScriptObject
        /// </summary>
        /// <param name="container">The script container to attach the KScriptObject to.</param>
        public void Init(KScriptContainer container) => SetContainer(container);

        /// <summary>
        /// Validation code to ensure KScriptObject is correctly validated, throw KScriptInvalidScriptType or KScriptNoValidationNeeded.
        /// For correct validation, use a <see cref="KScriptValidator"/> to handle appropraite exceptions and property validations.
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
