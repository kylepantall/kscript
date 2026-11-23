using KScript.Syntax.Markup;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        private List<(MarkupElement Element, Scope Body, bool Markup)> testBodies = new();

        // TestRunner registers the entire file before executing a test. Its before_each hooks
        // execute outermost first, in the same local scope as the test and its teardown.
        private void WalkTestBodies()
        {
            var registered = testBodies;
            testBodies = null;
            if (registered.Count == 0) return;
            var setup = new Dictionary<Scope, List<Symbol>>();
            foreach (var (element, body, markup) in registered.OrderBy(entry => entry.Element.Name == "before_each"
                ? entry.Element.Ancestors().Count() : int.MaxValue))
            {
                // A conditionally registered hook is only guaranteed on its own registration path.
                var parents = new Stack<Scope>();
                for (Scope scope = body.Parent; scope != null; scope = scope.Parent) parents.Push(scope);
                foreach (Scope scope in parents)
                {
                    if (setup.TryGetValue(scope, out var symbols))
                        foreach (Symbol symbol in symbols) body.Declare(symbol, body.Span.Start);
                }

                int inherited = body.Symbols.Count;
                WalkChildren(element.Children, body, markup);
                if (element.Name == "before_each" && body.Symbols.Count > inherited)
                {
                    if (!setup.TryGetValue(body.Parent, out var symbols)) setup[body.Parent] = symbols = new();
                    symbols.AddRange(body.Symbols.Skip(inherited).Select(binding => binding.Symbol));
                }
            }
        }
    }
}
