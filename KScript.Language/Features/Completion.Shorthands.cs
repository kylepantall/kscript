using KScript.Language.Analysis;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        /// <summary>
        /// In an <c>on-</c> handler, where a shorthand's name can go (the start, or after a comma in a handler of
        /// shorthands): the actions, and the companion the last action takes (<c>by:</c> after increment:).
        /// Null anywhere else.
        /// </summary>
        private static List<CompletionItem> ShorthandCompletions(DocumentAnalysis analysis, Cursor cursor)
        {
            if (cursor.Attribute is not { Value: not null } attribute || !attribute.Name.StartsWith("on-", StringComparison.Ordinal)) return null;
            DecodedText value = attribute.Value;
            int local = value.FromSource(cursor.Offset);
            if (local < 0 || local > value.Value.Length) return null;
            string before = value.Value[..local];
            int wordStart = local;
            while (wordStart > 0 && (char.IsLetterOrDigit(before[wordStart - 1]) || before[wordStart - 1] == '_')) wordStart--;
            string lead = before[..wordStart].TrimEnd();
            bool atStart = lead.Length == 0;
            if (!atStart && !(lead.EndsWith(',') && HandlerShorthands.IsShorthand(value.Value))) return null;

            var replace = TextSpan.FromBounds(value.ToSource(wordStart), cursor.Offset);
            var items = HandlerShorthands.Actions.Select(a => new CompletionItem
            {
                Label = a.Name + ":",
                FilterText = a.Name,
                Kind = CompletionKind.Keyword,
                Detail = "shorthand",
                Documentation = a.Description,
                InsertText = a.Name + ": ",
                ReplaceSpan = replace,
                SortText = "0" + a.Name,
            }).ToList();
            // Modifiers (confirm:, key:, debounce:) go with the actions; each once.
            HandlerShorthands.Reading written = atStart ? null : HandlerShorthands.Read(before[..lead.Length].TrimEnd(','));
            items.AddRange(HandlerShorthands.Modifiers.Where(m => written?.Modifiers.Any(w => w.Modifier == m) != true).Select(m => new CompletionItem
            {
                Label = m.Name + ":",
                FilterText = m.Name,
                Kind = CompletionKind.Keyword,
                Detail = "modifier",
                Documentation = m.Description,
                InsertText = m.Name + ": ",
                ReplaceSpan = replace,
                SortText = "1" + m.Name,
            }));
            if (!atStart && HandlerShorthands.Read(before[..lead.Length].TrimEnd(',')).Steps.LastOrDefault() is { Companion: null } last && last.Action.Companion != null)
            {
                items.Insert(0, new CompletionItem
                {
                    Label = last.Action.Companion + ":",
                    FilterText = last.Action.Companion,
                    Kind = CompletionKind.Keyword,
                    Detail = "goes with " + last.Action.Name + ":",
                    Documentation = last.Action.Description,
                    InsertText = last.Action.Companion + ": ",
                    ReplaceSpan = replace,
                    SortText = "00" + last.Action.Companion,
                });
            }
            return items;
        }

        /// <summary>Hover on a shorthand's name in a handler: what it does and the statement it stands for.</summary>
        private static HoverResult ShorthandHover(Cursor cursor)
        {
            if (cursor.Attribute is not { Value: not null } attribute || !attribute.Name.StartsWith("on-", StringComparison.Ordinal)
                || !HandlerShorthands.IsShorthand(attribute.Value.Value)) return null;
            DecodedText value = attribute.Value;
            int local = value.FromSource(cursor.Offset);
            HandlerShorthands.Reading reading = HandlerShorthands.Read(value.Value);
            foreach ((HandlerShorthands.Modifier modifier, var argument) in reading.Modifiers)
            {
                if (local >= argument.NameSpan.Start && local <= argument.NameSpan.End)
                    return new HoverResult($"**{modifier.Name}:** (handler modifier)\n\n{modifier.Description}", value.ToSource(argument.NameSpan));
            }
            foreach (HandlerShorthands.Step step in reading.Steps)
            {
                foreach (var argument in new[] { step.Value, step.Companion }.Where(a => a != null))
                {
                    if (local < argument.NameSpan.Start || local > argument.NameSpan.End) continue;
                    string same = HandlerShorthands.Statement(step, value.Value) is (string statement, _) ? $"\n\nSame as `{statement}`." : string.Empty;
                    return new HoverResult($"**{step.Action.Name}:** (handler shorthand)\n\n{step.Action.Description}{same}", value.ToSource(argument.NameSpan));
                }
            }
            return null;
        }
    }
}
