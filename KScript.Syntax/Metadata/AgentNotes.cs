namespace KScript.Metadata
{
    /// <summary>
    /// What a coding agent should know about a command, method or element, beyond its description: the one line
    /// to read (<see cref="Llm"/>), the way to write it (<see cref="Prefer"/>), what not to do (<see cref="Avoid"/>)
    /// and the mistake models make with it (<see cref="Pitfall"/>). Packages add them with
    /// <c>PackageBuilder.AgentNotes</c>; for a user's own functions they come from the <c>@llm</c>, <c>@prefer</c>,
    /// <c>@avoid</c> and <c>@pitfall</c> doc tags. <c>kscript agents</c> writes them into AGENTS.md and .kscript/agents.
    /// </summary>
    public sealed class AgentNotes
    {
        public AgentNotes(string llm = null, string prefer = null, string avoid = null, string pitfall = null)
        {
            Llm = llm;
            Prefer = prefer;
            Avoid = avoid;
            Pitfall = pitfall;
        }

        /// <summary>The line an agent reads instead of the description, when the description is long or human-shaped.</summary>
        public string Llm { get; }

        public string Prefer { get; }

        public string Avoid { get; }

        public string Pitfall { get; }
    }
}
