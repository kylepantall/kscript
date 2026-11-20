namespace KScript.Documents.Maths
{
    /// <summary>The LaTeX maths commands KScript knows: symbols, operators, functions, accents, fonts and spaces.</summary>
    internal static class MathSymbols
    {
        /// <summary>Symbol commands: \alpha is α, an ordinary atom; \leq is ≤, a relation.</summary>
        public static readonly Dictionary<string, (string Text, AtomKind Kind)> Symbols = new Dictionary<string, (string, AtomKind)>(StringComparer.Ordinal)
        {
            // Greek, lower case
            ["alpha"] = ("α", AtomKind.Ord), ["beta"] = ("β", AtomKind.Ord), ["gamma"] = ("γ", AtomKind.Ord),
            ["delta"] = ("δ", AtomKind.Ord), ["epsilon"] = ("ε", AtomKind.Ord), ["varepsilon"] = ("ε", AtomKind.Ord),
            ["zeta"] = ("ζ", AtomKind.Ord), ["eta"] = ("η", AtomKind.Ord), ["theta"] = ("θ", AtomKind.Ord),
            ["vartheta"] = ("ϑ", AtomKind.Ord), ["iota"] = ("ι", AtomKind.Ord), ["kappa"] = ("κ", AtomKind.Ord),
            ["lambda"] = ("λ", AtomKind.Ord), ["mu"] = ("μ", AtomKind.Ord), ["nu"] = ("ν", AtomKind.Ord),
            ["xi"] = ("ξ", AtomKind.Ord), ["pi"] = ("π", AtomKind.Ord), ["varpi"] = ("ϖ", AtomKind.Ord),
            ["rho"] = ("ρ", AtomKind.Ord), ["varrho"] = ("ρ", AtomKind.Ord), ["sigma"] = ("σ", AtomKind.Ord),
            ["varsigma"] = ("ς", AtomKind.Ord), ["tau"] = ("τ", AtomKind.Ord), ["upsilon"] = ("υ", AtomKind.Ord),
            ["phi"] = ("ϕ", AtomKind.Ord), ["varphi"] = ("φ", AtomKind.Ord), ["chi"] = ("χ", AtomKind.Ord),
            ["psi"] = ("ψ", AtomKind.Ord), ["omega"] = ("ω", AtomKind.Ord),

            // Greek, upper case
            ["Gamma"] = ("Γ", AtomKind.Ord), ["Delta"] = ("Δ", AtomKind.Ord), ["Theta"] = ("Θ", AtomKind.Ord),
            ["Lambda"] = ("Λ", AtomKind.Ord), ["Xi"] = ("Ξ", AtomKind.Ord), ["Pi"] = ("Π", AtomKind.Ord),
            ["Sigma"] = ("Σ", AtomKind.Ord), ["Upsilon"] = ("Υ", AtomKind.Ord), ["Phi"] = ("Φ", AtomKind.Ord),
            ["Psi"] = ("Ψ", AtomKind.Ord), ["Omega"] = ("Ω", AtomKind.Ord),

            // Binary operators
            ["pm"] = ("±", AtomKind.Bin), ["mp"] = ("∓", AtomKind.Bin), ["times"] = ("×", AtomKind.Bin),
            ["div"] = ("÷", AtomKind.Bin), ["cdot"] = ("⋅", AtomKind.Bin), ["ast"] = ("∗", AtomKind.Bin),
            ["star"] = ("⋆", AtomKind.Bin), ["circ"] = ("∘", AtomKind.Bin), ["bullet"] = ("∙", AtomKind.Bin),
            ["cup"] = ("∪", AtomKind.Bin), ["cap"] = ("∩", AtomKind.Bin), ["wedge"] = ("∧", AtomKind.Bin),
            ["land"] = ("∧", AtomKind.Bin), ["vee"] = ("∨", AtomKind.Bin), ["lor"] = ("∨", AtomKind.Bin),
            ["oplus"] = ("⊕", AtomKind.Bin), ["otimes"] = ("⊗", AtomKind.Bin), ["setminus"] = ("∖", AtomKind.Bin),

            // Relations
            ["leq"] = ("≤", AtomKind.Rel), ["le"] = ("≤", AtomKind.Rel), ["geq"] = ("≥", AtomKind.Rel),
            ["ge"] = ("≥", AtomKind.Rel), ["neq"] = ("≠", AtomKind.Rel), ["ne"] = ("≠", AtomKind.Rel),
            ["approx"] = ("≈", AtomKind.Rel), ["equiv"] = ("≡", AtomKind.Rel), ["sim"] = ("∼", AtomKind.Rel),
            ["simeq"] = ("≃", AtomKind.Rel), ["cong"] = ("≅", AtomKind.Rel), ["propto"] = ("∝", AtomKind.Rel),
            ["in"] = ("∈", AtomKind.Rel), ["notin"] = ("∉", AtomKind.Rel), ["ni"] = ("∋", AtomKind.Rel),
            ["subset"] = ("⊂", AtomKind.Rel), ["subseteq"] = ("⊆", AtomKind.Rel), ["supset"] = ("⊃", AtomKind.Rel),
            ["supseteq"] = ("⊇", AtomKind.Rel), ["to"] = ("→", AtomKind.Rel), ["rightarrow"] = ("→", AtomKind.Rel),
            ["leftarrow"] = ("←", AtomKind.Rel), ["gets"] = ("←", AtomKind.Rel), ["leftrightarrow"] = ("↔", AtomKind.Rel),
            ["Rightarrow"] = ("⇒", AtomKind.Rel), ["Leftarrow"] = ("⇐", AtomKind.Rel), ["Leftrightarrow"] = ("⇔", AtomKind.Rel),
            ["implies"] = ("⇒", AtomKind.Rel), ["iff"] = ("⇔", AtomKind.Rel), ["mapsto"] = ("↦", AtomKind.Rel),
            ["uparrow"] = ("↑", AtomKind.Rel), ["downarrow"] = ("↓", AtomKind.Rel), ["perp"] = ("⊥", AtomKind.Rel),
            ["parallel"] = ("∥", AtomKind.Rel), ["mid"] = ("∣", AtomKind.Rel), ["ll"] = ("≪", AtomKind.Rel),
            ["gg"] = ("≫", AtomKind.Rel), ["models"] = ("⊨", AtomKind.Rel), ["vdash"] = ("⊢", AtomKind.Rel),
            ["coloneqq"] = ("≔", AtomKind.Rel),

            // Ordinary symbols
            ["infty"] = ("∞", AtomKind.Ord), ["partial"] = ("∂", AtomKind.Ord), ["nabla"] = ("∇", AtomKind.Ord),
            ["forall"] = ("∀", AtomKind.Ord), ["exists"] = ("∃", AtomKind.Ord), ["neg"] = ("¬", AtomKind.Ord),
            ["lnot"] = ("¬", AtomKind.Ord), ["emptyset"] = ("∅", AtomKind.Ord), ["varnothing"] = ("∅", AtomKind.Ord),
            ["hbar"] = ("ℏ", AtomKind.Ord), ["ell"] = ("ℓ", AtomKind.Ord), ["Re"] = ("ℜ", AtomKind.Ord),
            ["Im"] = ("ℑ", AtomKind.Ord), ["aleph"] = ("ℵ", AtomKind.Ord), ["angle"] = ("∠", AtomKind.Ord),
            ["prime"] = ("′", AtomKind.Ord), ["ldots"] = ("…", AtomKind.Ord), ["dots"] = ("…", AtomKind.Ord),
            ["cdots"] = ("⋯", AtomKind.Ord), ["vdots"] = ("⋮", AtomKind.Ord), ["ddots"] = ("⋱", AtomKind.Ord),
            ["top"] = ("⊤", AtomKind.Ord), ["bot"] = ("⊥", AtomKind.Ord), ["triangle"] = ("△", AtomKind.Ord),
            ["therefore"] = ("∴", AtomKind.Ord), ["because"] = ("∵", AtomKind.Ord), ["degree"] = ("°", AtomKind.Ord),
            ["checkmark"] = ("✓", AtomKind.Ord),

            // Delimiters
            ["{"] = ("{", AtomKind.Open), ["}"] = ("}", AtomKind.Close), ["lbrace"] = ("{", AtomKind.Open), ["rbrace"] = ("}", AtomKind.Close),
            ["langle"] = ("⟨", AtomKind.Open), ["rangle"] = ("⟩", AtomKind.Close), ["lfloor"] = ("⌊", AtomKind.Open),
            ["rfloor"] = ("⌋", AtomKind.Close), ["lceil"] = ("⌈", AtomKind.Open), ["rceil"] = ("⌉", AtomKind.Close),
            ["|"] = ("‖", AtomKind.Ord), ["vert"] = ("|", AtomKind.Ord), ["Vert"] = ("‖", AtomKind.Ord),
            ["lvert"] = ("|", AtomKind.Open), ["rvert"] = ("|", AtomKind.Close),
            ["lVert"] = ("‖", AtomKind.Open), ["rVert"] = ("‖", AtomKind.Close),

            // Escaped characters
            ["%"] = ("%", AtomKind.Ord), ["$"] = ("$", AtomKind.Ord), ["#"] = ("#", AtomKind.Ord), ["&"] = ("&", AtomKind.Ord), ["_"] = ("_", AtomKind.Ord),
        };

        /// <summary>Large operators: (character, scripts above and below in displayed maths).</summary>
        public static readonly Dictionary<string, (string Text, bool Limits)> BigOps = new Dictionary<string, (string, bool)>(StringComparer.Ordinal)
        {
            ["sum"] = ("∑", true), ["prod"] = ("∏", true), ["coprod"] = ("∐", true),
            ["int"] = ("∫", false), ["iint"] = ("∬", false), ["iiint"] = ("∭", false), ["oint"] = ("∮", false),
            ["bigcup"] = ("⋃", true), ["bigcap"] = ("⋂", true), ["bigoplus"] = ("⨁", true), ["bigotimes"] = ("⨂", true),
        };

        /// <summary>Named operators set upright: (name, scripts above and below in displayed maths).</summary>
        public static readonly Dictionary<string, bool> Functions = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["sin"] = false, ["cos"] = false, ["tan"] = false, ["cot"] = false, ["sec"] = false, ["csc"] = false,
            ["arcsin"] = false, ["arccos"] = false, ["arctan"] = false, ["sinh"] = false, ["cosh"] = false, ["tanh"] = false,
            ["log"] = false, ["ln"] = false, ["lg"] = false, ["exp"] = false, ["det"] = true, ["dim"] = false, ["ker"] = false,
            ["deg"] = false, ["arg"] = false, ["gcd"] = true, ["hom"] = false, ["Pr"] = true,
            ["lim"] = true, ["limsup"] = true, ["liminf"] = true, ["max"] = true, ["min"] = true, ["sup"] = true, ["inf"] = true,
            ["argmax"] = true, ["argmin"] = true,
        };

        public static readonly Dictionary<string, double> Spaces = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [","] = 3 / 18.0, [":"] = 4 / 18.0, [">"] = 4 / 18.0, [";"] = 5 / 18.0, ["!"] = -3 / 18.0, [" "] = 1 / 3.0,
            ["quad"] = 1, ["qquad"] = 2, ["enspace"] = 0.5, ["thinspace"] = 3 / 18.0,
        };

        public static readonly HashSet<string> Accents = new HashSet<string>(StringComparer.Ordinal)
        {
            "hat", "widehat", "bar", "vec", "dot", "ddot", "tilde", "widetilde", "overline", "underline",
        };

        public static readonly Dictionary<string, MathFont> Fonts = new Dictionary<string, MathFont>(StringComparer.Ordinal)
        {
            ["mathrm"] = MathFont.Roman, ["mathbf"] = MathFont.Bold, ["mathit"] = MathFont.Italic, ["mathsf"] = MathFont.Sans,
            ["mathtt"] = MathFont.Mono, ["boldsymbol"] = MathFont.Bold, ["bm"] = MathFont.Bold,
            // No blackboard or calligraphic font is built in: \mathbb{R} is bold and \mathcal{L} italic in PDF output.
            ["mathbb"] = MathFont.Bold, ["mathcal"] = MathFont.Italic, ["mathfrak"] = MathFont.Italic, ["mathscr"] = MathFont.Italic,
        };

        public static readonly HashSet<string> TextCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            "text", "textrm", "textnormal", "mbox", "textbf", "textit", "texttt", "textsf",
        };

        public static readonly HashSet<string> Environments = new HashSet<string>(StringComparer.Ordinal)
        {
            "matrix", "pmatrix", "bmatrix", "Bmatrix", "vmatrix", "Vmatrix", "cases", "aligned", "align", "align*",
            "gathered", "split", "array",
        };

        /// <summary>Commands that change nothing here: sizes and styles TeX uses for spacing.</summary>
        public static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            "displaystyle", "textstyle", "scriptstyle", "scriptscriptstyle", "big", "Big", "bigg", "Bigg", "bigl", "bigr",
            "Bigl", "Bigr", "biggl", "biggr", "Biggl", "Biggr", "nonumber", "notag", "label",
        };

        /// <summary>The LaTeX command for a character typed directly, so α in a document becomes \alpha in a .tex file.</summary>
        public static readonly Dictionary<string, string> CommandFor = BuildCommandFor();

        private static Dictionary<string, string> BuildCommandFor()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in Symbols)
            {
                if (kv.Key.Length > 1 && !map.ContainsKey(kv.Value.Text)) map[kv.Value.Text] = "\\" + kv.Key;
            }
            foreach (var kv in BigOps) map.TryAdd(kv.Value.Text, "\\" + kv.Key);
            map["−"] = "-";
            return map;
        }

        /// <summary>The kind of a character typed directly: + is binary, = a relation, ( opens.</summary>
        public static AtomKind KindOf(char c) => c switch
        {
            '+' or '-' or '*' or '−' or '±' or '×' or '÷' or '⋅' => AtomKind.Bin,
            '=' or '<' or '>' or ':' or '≤' or '≥' or '≠' or '≈' or '≡' or '→' or '∈' => AtomKind.Rel,
            '(' or '[' => AtomKind.Open,
            ')' or ']' => AtomKind.Close,
            ',' or ';' => AtomKind.Punct,
            _ => AtomKind.Ord,
        };
    }
}
