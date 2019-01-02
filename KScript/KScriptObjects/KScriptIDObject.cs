namespace KScript.KScriptObjects
{
    public abstract class KScriptIDObject : KScriptObject
    {
        [KScriptProperty("Defines the Unique ID for this KScript Object.", true)]
        public string id { get; set; }

        /// <summary>
        /// Used to register the current object into the ObjectStorage Container.
        /// </summary>
        public void RegisterObject() => ParentContainer.GetObjectStorageContainer().AddObjectFromUID(id, this);
    }
}