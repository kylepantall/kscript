using System.Runtime.CompilerServices;

using KScript.Syntax;

namespace KScript.Runtime
{
    public sealed partial class Interpreter
    {
        // Repeated short inputs benefit most. Large strings and failed checks aren't retained.
        internal const int MaxCachedTextLength = 256;

        private readonly ConditionalWeakTable<CastNode, CastSite> castSites = new();

        private sealed class CastSite
        {
            internal CastSite(CastNode node)
            {
                // Inline rule arguments bind to a scope; keep only immutable type references here.
                if (!node.TypeName.Contains('(')) Target = TypeRef.Parse(node.TypeName);
            }

            internal readonly TypeRef Target;
            private ConditionalWeakTable<RefinedType, RecentCast> values;

            internal RecentCast For(RefinedType type) =>
                LazyInitializer.EnsureInitialized(ref values, static () => new ConditionalWeakTable<RefinedType, RecentCast>())
                    .GetValue(type, static _ => new RecentCast());
        }

        private readonly ConditionalWeakTable<RegexValue, RecentMatch> textMatches = new();

        private sealed class RecentCast { internal volatile PatternLiteral Last; }
        private sealed class RecentMatch { internal volatile string Text; }

        private object ConvertRepeatedText(CastSite site, RefinedType type, string text)
        {
            // A type lives with its module. Sharing a parsed node across scopes must not share
            // checked text between two different declarations with the same name.
            RecentCast cache = site.For(type);
            PatternLiteral previous = cache.Last;
            if (previous?.Word == text) return previous.Convert(this);

            var literal = new PatternLiteral(text, type);
            object value = literal.Convert(this);
            cache.Last = literal;
            return value;
        }

        internal bool MatchesRefinement(RegexValue pattern, string text)
        {
            if (text.Length > MaxCachedTextLength) return pattern.Regex.IsMatch(text);
            RecentMatch cache = textMatches.GetValue(pattern, static _ => new RecentMatch());
            if (cache.Text == text) return true;
            if (!pattern.Regex.IsMatch(text)) return false;
            cache.Text = text;
            return true;
        }
    }
}
