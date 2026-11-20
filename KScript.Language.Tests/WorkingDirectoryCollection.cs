namespace KScript.Language.Tests
{
    /// <summary>
    /// Tests that change the process's current directory. They run on their own: while one runs, a path like
    /// "/tmp/x" in another test resolves against the current directory's drive on Windows, so it would move.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class WorkingDirectoryCollection
    {
        public const string Name = "Working directory";
    }
}
