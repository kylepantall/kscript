using System.Runtime.CompilerServices;

namespace KScript.Runtime
{
    /// <summary>
    /// Work that runs when a web request has been answered, keyed by the request's <c>$request</c> map. A package
    /// that keeps state for a request (KScript.Auth's session) adds a step; the server that answered the request
    /// (a route or a page) runs the steps and adds the Set-Cookie headers they return to the response.
    /// </summary>
    public static class RequestHooks
    {
        private static readonly ConditionalWeakTable<Dictionary<string, object>, List<Func<IEnumerable<string>>>> Steps =
            new ConditionalWeakTable<Dictionary<string, object>, List<Func<IEnumerable<string>>>>();

        /// <summary>
        /// Runs <paramref name="step"/> once the request is answered. It returns Set-Cookie header values (or none).
        /// </summary>
        public static void OnFinish(Dictionary<string, object> request, Func<IEnumerable<string>> step)
        {
            List<Func<IEnumerable<string>>> steps = Steps.GetOrCreateValue(request);
            lock (steps) steps.Add(step);
        }

        /// <summary>Runs the request's steps, once, and returns the cookies they set.</summary>
        public static List<string> Finish(Dictionary<string, object> request)
        {
            var cookies = new List<string>();
            if (request == null || !Steps.TryGetValue(request, out List<Func<IEnumerable<string>>> steps)) return cookies;
            Steps.Remove(request);
            foreach (Func<IEnumerable<string>> step in steps) cookies.AddRange(step() ?? Enumerable.Empty<string>());
            return cookies;
        }
    }
}
