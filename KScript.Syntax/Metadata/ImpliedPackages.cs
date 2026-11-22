using System.Collections.Generic;

namespace KScript.Metadata
{
    /// <summary>
    /// Elements that bring in their package without an import, because they are keywords of the language:
    /// <c>job SendWelcome { }</c> needs KScript.Queue. The runtime and the
    /// editor both read this, so they agree on which files import what.
    /// </summary>
    public static class ImpliedPackages
    {
        public static readonly IReadOnlyDictionary<string, string> ByElement = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["job"] = "KScript.Queue",
        };
    }
}
