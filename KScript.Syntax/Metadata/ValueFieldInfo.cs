namespace KScript.Metadata
{
    /// <summary>
    /// A field of a built-in value, read with a dot and no brackets: <c>$response.status</c>, <c>$file.name</c>,
    /// <c>$date.year</c>. Fields are plain facts about the value; anything that takes arguments or does work is a
    /// method (<c>->name()</c>).
    /// </summary>
    public sealed class ValueFieldInfo
    {
        public ValueFieldInfo(string receiver, string name, string type, string description)
        {
            Receiver = receiver;
            Name = name;
            Type = type;
            Description = description;
        }

        /// <summary>The type of value the field is on, such as response or date.</summary>
        public string Receiver { get; }

        public string Name { get; }

        /// <summary>The type of the field's value, when known.</summary>
        public string Type { get; }

        public string Description { get; }

        /// <summary>The package that adds the field.</summary>
        public string Package { get; set; }

        /// <summary>A short usage example, shown in hover and run by the docs tests.</summary>
        public string Example { get; set; }
    }
}
