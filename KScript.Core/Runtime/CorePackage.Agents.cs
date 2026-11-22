using KScript.Metadata;

namespace KScript.Runtime
{
    public sealed partial class CorePackage
    {
        /// <summary>
        /// Notes for coding agents (<c>kscript agents</c>): the mistakes models make with core commands and methods,
        /// usually habits from other languages. One short line each, since agents read all of them.
        /// </summary>
        private static void RegisterAgentNotes(PackageBuilder p)
        {
            p.AgentNotes(
                ("list->push", new AgentNotes(pitfall: "Returns a new list and leaves the old one alone: write items = $items->push($x).")),
                ("map->set", new AgentNotes(pitfall: "Returns a new map: write person = $person->set('age', 3).")),
                ("list->each", new AgentNotes(prefer: "for item in $list { } for statements; ->each(fn) only calls fn and returns the list.")),
                ("list->sort", new AgentNotes(pitfall: "fn(a, b) returns a number (negative, 0, positive), not a bool.", prefer: "->sort_by(fn(p) => $p.age) to sort by a key.")),
                ("list->count", new AgentNotes(prefer: "->count(); there is no ->length(), .length property or len().")),
                ("string->count", new AgentNotes(prefer: "->count(); there is no ->length(), .length property or len().")),
                ("string->replace", new AgentNotes(pitfall: "Replaces every match, as plain text. Use ->replace_pattern for a regular expression.")),
                ("any->or", new AgentNotes(pitfall: "0, '' and [] count as missing too. Use $x ?? fallback to replace only null.")),
                ("date->format", new AgentNotes(pitfall: ".NET patterns: 'yyyy-MM-dd HH:mm' (MM is the month, mm the minutes).")),
                ("@range", new AgentNotes(pitfall: "The end is left out: @range(1, 11) is 1 to 10. So is 1..11.")),
                ("string->json", new AgentNotes(prefer: "$text->json(as: Type) to check the shape and get a typed record.")),
                ("string->to_number", new AgentNotes(prefer: "n: int = $text when a bad value should stop with a clear error; ->to_number(0) gives the default instead.")));
        }
    }
}
