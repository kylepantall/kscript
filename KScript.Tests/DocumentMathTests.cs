using KScript.Documents;
using KScript.Documents.Maths;
using KScript.Documents.Pdf;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class DocumentMathTests
{
    public static readonly TheoryData<string> Formulas = new TheoryData<string>
    {
        "x^2 + y_1 = z_{i,j}^{n+1}",
        "f'(x) + g''(x) = h^\\prime",
        "f'^2",
        "\\frac{a}{b} + \\dfrac{1}{2} - \\tfrac{3}{4} \\cdot \\cfrac{1}{1 + x}",
        "\\binom{n}{k} = \\dbinom{n}{k} = \\tbinom{n}{k}",
        "\\sqrt{2} \\sqrt[3]{x + 1}",
        "\\sum_{i=1}^{n} i^2 \\prod_{k} k \\coprod \\int_0^\\infty \\iint \\iiint \\oint_C \\bigcup_i A_i \\bigcap \\bigoplus \\bigotimes",
        "\\sum\\limits_{i} x \\int\\limits_0^1 \\lim\\nolimits_{x} y",
        "\\sin x + \\cos^2 \\theta + \\log_2 n + \\lim_{x \\to 0} \\frac{\\sin x}{x} + \\max_{i} a_i + \\operatorname{rank} A + \\operatorname*{argmax}_x f",
        "\\left( \\frac{a}{b} \\right) \\left[ x \\right] \\left\\{ y \\right\\} \\left| z \\right| \\left\\| w \\right\\| \\left\\langle v \\right\\rangle \\left. u \\right/ \\left\\lfloor t \\right\\rfloor \\left\\lceil s \\right\\rceil \\left\\lvert r \\right\\rvert \\left\\lVert q \\right\\rVert",
        "\\begin{matrix} a & b \\\\ c & d \\end{matrix} \\begin{pmatrix} 1 & 0 \\\\ 0 & 1 \\end{pmatrix} \\begin{bmatrix} x \\end{bmatrix} \\begin{Bmatrix} y \\end{Bmatrix} \\begin{vmatrix} a & b \\\\ c & d \\end{vmatrix} \\begin{Vmatrix} z \\end{Vmatrix}",
        "\\begin{cases} 1 & x > 0 \\\\ 0 & \\text{otherwise} \\end{cases}",
        "\\begin{aligned} a &= b + c \\\\ &= d \\end{aligned}",
        "\\begin{align} x &= 1 \\end{align} \\begin{align*} y &= 2 \\end{align*} \\begin{gathered} a \\\\ b \\end{gathered} \\begin{split} c &= d \\end{split}",
        "\\begin{array}{lcr} 1 & 2 & 3 \\\\ 4 & 5 & 6 \\end{array}",
        "\\begin{matrix}\n a & b\n c & d\n\\end{matrix}",
        "\\begin{pmatrix} {\\begin{matrix} 1 \\\\ 2 \\end{matrix}} \\end{pmatrix}",
        "\\hat{x} \\widehat{xy} \\bar{y} \\vec{v} \\dot{a} \\ddot{b} \\tilde{c} \\widetilde{cd} \\overline{AB} \\underline{z}",
        "\\mathbf{v} \\mathit{x} \\mathrm{d}x \\mathsf{S} \\mathtt{T} \\boldsymbol{\\alpha} \\bm{\\beta} \\mathbb{R} \\mathcal{L} \\mathfrak{g} \\mathscr{F}",
        "\\text{if } x \\textrm{ and } \\textnormal{n} \\mbox{m} \\textbf{bold} \\textit{it} \\texttt{tt} \\textsf{sf} \\text{a { b } c}",
        "a\\,b\\:c\\>d\\;e\\!f\\ g \\quad h \\qquad i \\enspace j \\thinspace k",
        "\\alpha \\beta \\Gamma \\Delta \\pi \\Omega \\infty \\partial \\nabla \\forall \\exists \\le \\ge \\ne \\approx \\equiv \\in \\notin \\subset \\subseteq \\cup \\cap \\pm \\mp \\times \\div \\to \\Rightarrow \\leftrightarrow \\ldots \\cdots \\vdots \\ddots \\ell \\hbar",
        "\\% \\$ \\# \\& \\_ \\{ \\} \\lbrace x \\rbrace \\langle a \\rangle | b \\| c \\vert d \\Vert",
        "\\displaystyle \\textstyle \\scriptstyle \\scriptscriptstyle \\big( \\Big[ \\bigg| \\Bigg\\| \\bigl( \\bigr) \\Bigl( \\Bigr) \\biggl( \\biggr) \\Biggl( \\Biggr) x \\nonumber \\notag",
        "α + β = γ ∑ − 2 × 3 ≤ 4",
        "1.5 + 2,000 - x * y / z : w ! ? ; [a] (b)",
        "{}^{14}C + x_{}",
        "e^{-x^2} 10^{10^{10}}",
        "\\frac{\\frac{a}{b}}{\\frac{c}{d}} \\sqrt{\\frac{x^2}{y_1}}",
        "\\left( \\begin{matrix} a \\\\ b \\\\ c \\end{matrix} \\right)",
        "\\mathbf{x^2 y} \\mathrm{\\mathbf{z}} x ~ y",
        "\\sum f \\int",
        "\\mathbf{\\sum} x",
        "",
    };

    [Theory]
    [MemberData(nameof(Formulas))]
    public void Every_writer_and_layout_handles(string latex)
    {
        MathNode node = MathParser.Parse(latex);
        Assert.Null(MathParser.Check(latex));
        string again = MathLatex.Write(node);
        // What the LaTeX writer writes parses back to the same LaTeX.
        Assert.Equal(again, MathLatex.Write(MathParser.Parse(again)));
        Assert.StartsWith("<math", MathMl.Write(node, display: true));
        Assert.StartsWith("<math", MathMl.Write(node, display: false));
        Assert.StartsWith("<m:oMath>", MathOmml.Write(node));
        Assert.NotNull(MathPlain.Write(node));
        foreach (FontFamily family in new[] { FontFamily.Serif, FontFamily.Sans })
        {
            foreach (bool display in new[] { true, false })
            {
                MathBox box = MathLayout.Layout(node, 10, display, "#000000", family);
                Assert.True(box.Width >= 0);
                foreach (Op op in box.Ops) Assert.NotNull(op.Moved(1, 2));
            }
        }
    }

    [Theory]
    [InlineData("x^", "a superscript is missing")]
    [InlineData("x^\\right)", "has no \\left")]
    [InlineData("x^\\end{a}", "has no \\begin")]
    [InlineData("x_", "is missing")]
    [InlineData("^2", "needs something before it")]
    [InlineData("_2", "needs something before it")]
    [InlineData("a & b", "& only works inside")]
    [InlineData("a \\\\ b", "starts a new line")]
    [InlineData("x^a^b", "two superscripts")]
    [InlineData("x_a_b", "two subscripts")]
    [InlineData("}", "without a {")]
    [InlineData("{x", "isn't closed")]
    [InlineData("\\frac{1}", "is missing")]
    [InlineData("\\frac 1", "denominator")]
    [InlineData("\\sqrt[3 x", "\\sqrt[ isn't closed")]
    [InlineData("\\left( x", "has no \\right")]
    [InlineData("\\left", "needs a delimiter")]
    [InlineData("\\left\\alpha x \\right)", "can't follow \\left")]
    [InlineData("\\left x \\right)", "'x' can't follow")]
    [InlineData("\\right)", "has no \\left")]
    [InlineData("\\end{matrix}", "has no \\begin")]
    [InlineData("\\begin{table} x \\end{table}", "isn't an environment")]
    [InlineData("\\begin{matrix} x", "has no \\end{matrix}")]
    [InlineData("\\begin{matrix} x \\end{pmatrix}", "is closed with \\end{pmatrix}")]
    [InlineData("\\begin{matrix} x } \\end{matrix}", "without a {")]
    [InlineData("\\begin{matrix} \\left( x \\end{matrix}", "has no \\right")]
    [InlineData("\\begin{matrix} x \\right) \\end{matrix}", "has no \\left")]
    [InlineData("\\nosuch", "isn't a maths command")]
    [InlineData("\\text{abc", "isn't closed")]
    [InlineData("x \\", "ends with a \\")]
    [InlineData("\\text x", "needs {...}")]
    [InlineData("\\mathbf", "is missing")]
    [InlineData("\\hat", "is missing")]
    [InlineData("\\operatorname x", "needs {...}")]
    public void Mistakes_name_the_problem(string latex, string message)
    {
        var e = Assert.Throws<KScriptException>(() => MathParser.Parse(latex));
        Assert.Contains(message, e.Message);
        Assert.Equal("Math", e.Type);
        Assert.Contains(message, MathParser.Check(latex));
    }

    [Fact]
    public void Long_maths_is_shortened_in_errors()
    {
        var e = Assert.Throws<KScriptException>(() => MathParser.Parse(new string('x', 80) + "}"));
        Assert.Contains("...", e.Message);
    }
}
