namespace KScript.Runtime
{
    /// <summary>
    /// A package registered something that breaks one of the language's naming or shape rules (see
    /// <see cref="Conventions"/>). It is thrown while the package loads, so the package can't be used until the name
    /// is fixed. The message names the package, what it registered and the rule.
    /// </summary>
    public sealed class ConventionException : InvalidOperationException
    {
        public ConventionException(string message) : base(message)
        {
        }
    }
}
