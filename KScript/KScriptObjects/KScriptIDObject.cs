namespace KScript.KScriptObjects
{
    public abstract class KScriptIDObject : KScriptObject
    {
        public KScriptIDObject() : base() { }
        public KScriptIDObject(string contents) : base(contents) { }
        public KScriptIDObject(object contents) : base(contents) { }

        [KScriptProperty("Defines the Unique ID for this KScript Object.", true)]
        public string id { get; set; }

        /// <summary>
        /// Used to register the current object into the ObjectStorage Container.
        /// </summary>
        public void RegisterObject() => ParentContainer.GetObjectStorageContainer().AddObjectFromUID(id, this);
    }
}