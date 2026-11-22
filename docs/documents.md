# Documents: KScript.Document

KScript.Document writes scientific and professional documents: papers, reports, letters and invoices. One script can save the same document as **PDF**, **Word** (.docx), **LaTeX** (.tex), **Markdown** and **HTML**. It has LaTeX maths, numbered sections, tables and figures, citations from BibTeX, footnotes and cross-references. The PDF is written by KScript itself, so you don't need TeX, Word or anything else installed.

```kscript
import KScript.Document

Document(template: paper, title: "How Gliders Move", author: "Ada Lovelace", to: "gliders.pdf") {
  Abstract "A glider returns to its own shape every \( p = 4 \) generations, one cell further on."

  Section(title: Introduction) {
    Paragraph "The Game of Life [@gardner1970] is played on a square grid. Each cell has **eight neighbours**."

    Math(id: eq-speed) "v = \frac{\max(|\Delta x|, |\Delta y|)}{p} \, c"

    Paragraph "Equation [#eq-speed] gives a pattern's speed."
  }

  Bibliography {
    Source(id: gardner1970, type: article, author: "Martin Gardner", title: "Mathematical Games", journal: "Scientific American", year: 1970)
  }
}
```

`import KScript.Document` brings in `Document` and the elements that go inside it. Everything inside a `Document` block is ordinary KScript, so `for`, `if`, variables and functions work between the elements: a table can come from data, and a section can repeat for each item of a list.

The [samples/documents](https://github.com/kylepantall/kscript/tree/master/samples/documents) folder has a full paper, a report, a letter and an invoice. `kscript make document` starts a new one.

## Saving

`to:` saves the document when the block ends. The extension picks the format, and a list of paths saves several formats:

```kscript
import KScript.Document

Document(title: "Notes", to: [notes.pdf, notes.docx, notes.tex]) {
  Paragraph "Saved three ways."
}
```

| Extension | What you get |
| --- | --- |
| `.pdf` | A PDF, laid out by KScript: page numbers, a clickable table of contents and bookmarks, links, and images. |
| `.docx` | A Word document with real headings, numbered lists, footnotes, equations Word can edit, and a table of contents Word fills in. |
| `.tex` | A LaTeX file for `pdflatex`, with `\section`, `\label`/`\ref`, `amsmath`, `booktabs` and `\bibitem`s. Images are referred to by path. |
| `.md` | Markdown with `$...$` maths, GitHub tables and footnotes. |
| `.html` | One HTML page: styles, MathML maths and images are all inside it. |

`into:` keeps the document in a variable instead (or as well), so you can save it later or turn it into text:

```kscript
import KScript.Document

Document(title: "Report", into: doc) {
  Paragraph "Hello."
}
$doc->save('report.pdf')
echo "{ $doc->pages() } pages, { $doc->words() } words"
```

| Method | What it does |
| --- | --- |
| `->save(path)` | Saves it, in the format the extension names. |
| `->pages()` | How many pages the PDF has. |
| `->words()` | How many words the text has, not counting maths and code. |
| `->markdown()`, `->latex()`, `->html()` | The document as text, to print, send or save yourself. |

`$doc.title` is the title.

## Templates

`template:` picks the overall look. Every setting under [Page and style](#page-and-style) can then change it.

| Template | What it is |
| --- | --- |
| `plain` | The default: serif text, numbered sections, the title at the top. |
| `paper` | A scientific paper: centred title, authors with affiliations, an abstract, indented paragraphs, numbered equations and references. |
| `report` | A title page, a table of contents, sans-serif headings in colour, each top-level section on a new page, and a footer with the title and page number. |
| `letter` | Use with the `Letter` element: addresses, date, subject, greeting and signature. |
| `invoice` | Use with the `Invoice` element: addresses, invoice details, an items table with tax and totals. |

`@document_templates()` lists them.

## Writing text

Text in `Paragraph`, `Item`, captions and titles may use:

| Write | For |
| --- | --- |
| `**bold**`, `*italic*` | Bold and italic (`__bold__` and `_italic_` too). |
| `` `code` `` | Code. |
| `[text](https://...)` | A link. |
| `\( x^2 \)` | Maths in the line. `\[ ... \]` also works. |
| `[@key]`, `[@a; @b]` | A citation of a source in the [bibliography](#citations-and-references). |
| `[#id]` | A cross-reference: the number of the section, table, figure or equation with that `id`. |
| `^[text]` | A footnote. |
| `--`, `---` | An en dash and an em dash. |

A blank line in the text starts a new paragraph. `{ }` puts in a value as anywhere else in KScript, and `$name` puts in a variable, so maths uses `\( \)` rather than `$`. Inside `\( \)` braces are kept for LaTeX: `\( \frac{1}{2} \)` is a fraction, not KScript.

`Paragraph(lines: true)` keeps the line breaks, as in an address or a poem. `align:` (left, center, right, justify) overrides the document's alignment.

## Structure

```kscript
import KScript.Document

Document(template: report, title: "Annual Report", author: "The team", to: "report.pdf") {
  Contents

  Section(title: Summary, id: summary) {
    Paragraph "Visits rose every quarter."

    Section(title: Details) {
      List(style: number) {
        Item "First point."
        Item "Second point."
        List {
          Item "A point under the second."
        }
      }
    }
  }

  Section(title: Appendix, numbered: false, new_page: true) {
    Quote(by: "John Conway") "I'm not a mathematician who's interested in applications."
    Code(language: python) "print('hello')"
  }
}
```

| Element | What it is |
| --- | --- |
| `Author(affiliation, email)` | An author. Its text is the name. Or give `author: "Ada Lovelace; Alan Turing"` on the document. |
| `Abstract` | The summary under the title. |
| `Section(title, id, numbered, new_page)` | A section. A section inside a section is a subsection, down to six levels. |
| `Paragraph(id, align, lines)` | A paragraph. |
| `List(style, start)` and `Item` | A list: `bullet`, `number`, `letter` or `roman`. A `List` right after an `Item` is nested in it. |
| `Quote(by)` | A block quotation. |
| `Code(language)` | Code, in a fixed-width font, kept exactly as written. |
| `Contents(title, depth)` | A table of contents, with page numbers in the PDF. |
| `Markdown(src)` | Markdown from its text or a file: [see below](#markdown). |
| `PageBreak`, `VSpace(height)`, `Rule` | A new page, a gap such as `1cm`, and a line across the page. |

## Maths

`Math` is an equation on its own line. Its text is LaTeX, kept exactly as written, so braces and backslashes need no escaping. A long equation can go in a ```` ```latex ```` block over several lines, and a long `Paragraph` can end its lines with `\` (see [Long strings and blocks of text](code-format.md#long-strings-and-blocks-of-text)). Equations are numbered, and `id:` lets the text refer to the number with `[#id]`:

```kscript
import KScript.Document

Document(title: "Maths", to: "maths.pdf") {
  Math(id: eq-gauss) "\int_{-\infty}^{\infty} e^{-x^2} \, dx = \sqrt{\pi}"
  Math(numbered: false) "\sum_{k=1}^{n} k = \frac{n(n+1)}{2}"
  Math "A = \begin{pmatrix} a & b \\\\ c & d \end{pmatrix}, \quad \det A = ad - bc"
}
```

In a `"..."` string `\\` is how you write one backslash, so a LaTeX row break `\\` is written `\\\\`. A new line inside an environment also ends a row:

```kscript
import KScript.Document

Document(title: "Cases", to: "cases.pdf") {
  Math "|x| = \begin{cases}
    x & \text{if } x \ge 0
    -x & \text{otherwise}
  \end{cases}"
}
```

What the maths understands:

- Letters, numbers and operators with TeX's spacing; `^` and `_` for superscripts and subscripts; `'` for primes.
- `\frac`, `\dfrac`, `\tfrac`, `\binom`, `\sqrt`, `\sqrt[n]`.
- Greek letters (`\alpha` to `\Omega`), relations (`\le`, `\approx`, `\in`, `\subseteq`, `\to`...), operators (`\pm`, `\times`, `\cdot`, `\cup`...), arrows, and symbols such as `\infty`, `\partial`, `\nabla`, `\forall`, `\exists`.
- Large operators with limits: `\sum`, `\prod`, `\int`, `\iint`, `\oint`, `\bigcup`...; functions such as `\sin`, `\log`, `\lim`, `\max`, and `\operatorname{name}`.
- `\left( ... \right)` and the other delimiters, which grow to fit.
- `matrix`, `pmatrix`, `bmatrix`, `vmatrix`, `cases`, `aligned` and `array` environments.
- `\text{...}`, `\mathbf`, `\mathit`, `\mathrm`, `\mathbb`, `\mathcal`, `\mathsf`, `\mathtt`; accents such as `\hat`, `\bar`, `\vec`, `\dot`, `\tilde`, `\overline`, `\underline`; and spaces `\,`, `\;`, `\quad`, `\qquad`.

Unknown commands are errors that name the command, so a typo is caught when the script runs rather than in the finished PDF. Each format gets the maths in its own form: drawn in the PDF, `amsmath` in LaTeX, Word equations in .docx, and MathML in HTML.

## Tables

A table can come from data, which is usually a list of maps:

```kscript
people = [{ name: 'Ada', born: 1815 }, { name: 'Alan', born: 1912 }]

import KScript.Document

Document(title: "People", to: "people.pdf") {
  Table(caption: "Two mathematicians.", id: tab-people, rows: $people, columns: { Name: 'name', Born: 'born' }, align: ['left', 'right'])
}
```

`columns:` is a map from heading to key (or a list of keys, which are also the headings). Without it the first map's keys are the columns. Numbers line up on the right unless `align:` says otherwise.

Or write the rows with `Tr`, cells separated by `|`:

```kscript
import KScript.Document

Document(title: "Scores", to: "scores.pdf") {
  Table(caption: "Scores.", align: ['left', 'right', 'right'], widths: [3, 1, 1], rules: grid) {
    Tr(header: true) "Team | Played | Points"
    Tr "Red | 3 | 7"
    Tr "Blue | 3 | **9**"
  }
}
```

`widths:` makes the table as wide as the text, sharing the width in those proportions. Without it, columns are as wide as their text. `rules:` is `booktabs` (lines above, below and under the header, the default), `grid` or `none`; `size: 0.9` makes the text smaller. A caption numbers the table, and its heading row repeats when a table runs onto a new page.

## Figures

```kscript
import KScript.Document

Document(title: "Gliders", to: "gliders.pdf") {
  Figure(path: "glider.png", caption: "A glider over four generations.", id: fig-glider, width: 0.8)
}
```

`path:` is a PNG or JPEG, relative to the script. `width:` is the share of the text width (`0.5` or `'50%'`). Figures with captions are numbered for `[#id]`.

## Citations and references

Cite with `[@key]`. Sources come from a BibTeX file, from `Source` elements, or both:

```kscript
import KScript.Document

Document(template: paper, title: "Cited", to: "cited.pdf") {
  Paragraph "Life is universal [@berlekamp1982; @rendell2002]."

  Bibliography(path: "refs.bib") {
    Source(id: rendell2002, type: inproceedings, author: "Paul Rendell", title: "Turing Universality of the Game of Life", booktitle: "Collision-Based Computing", year: 2002)
  }
}
```

Only cited sources are listed. With `citations: numeric` (the default) they are numbered in the order they are first cited, as [1]. With `citations: author-year` they read (Berlekamp et al., 1982) and the list is sorted by author. A `Source` takes any BibTeX field as an attribute; `author` separates people with `and`. If you cite sources but leave out `Bibliography`, the references go at the end.

A citation or cross-reference to an id that doesn't exist is an error, as is the same id used twice.

## Footnotes and cross-references

`^[text]` puts a numbered footnote at the foot of the page (in Word, a real footnote; in HTML and Markdown, notes at the end). `[#id]` is replaced by the number of the section, table, figure or equation with that id, so you write the word yourself: `Section [#methods]` reads "Section 2", and `Equation [#eq-speed]` reads "Equation (3)". In the PDF and Word, the number is a link.

## Markdown

`Markdown` adds Markdown as document content: `#` headings become sections (nested by level), with paragraphs, lists, tables, code blocks, quotes, `---` rules and `![caption](image.png){#id}` figures. Headings and maths can take ids: `## Results {#results}`, `\[ E = mc^2 \] {#eq-energy}`.

```kscript
import KScript.Document

Document(title: "From Markdown", to: "notes.pdf") {
  Markdown(path: "chapters/introduction.md")
  Markdown "
    ## Summary

    - Every chapter is a Markdown file.
    - The script puts them together.
  "
}
```

In Markdown files, `$x$` and `$$ ... $$` are maths too. In text inside a script, use `\( \)` and `\[ \]`, since `$` is a variable there.

## Letters and invoices

`Letter` lays out a letter around the paragraphs inside it. In addresses, `\n` starts a new line:

```kscript
import KScript.Document

Document(template: letter, to: "letter.pdf") {
  Letter(from: "Life Lab\n12 Grid Street\nCambridge", to: "Dr Ada Lovelace\nExample University", subject: "Invitation to speak", opening: "Dear Dr Lovelace,", closing: "Yours sincerely,", signature: "Alan Turing") {
    Paragraph "We would be delighted if you would give the opening talk."
  }
}
```

`Invoice` makes the whole invoice from a list of items. Each item has a `description`, a `quantity` (default 1) and a `price`, or an `amount`. The subtotal, tax and total are worked out for you:

```kscript
items = [{ description: 'Consulting', quantity: 3, price: 450 }, { description: 'Travel', price: 64.2 }]

import KScript.Document

Document(template: invoice, to: "invoice.pdf") {
  Invoice(number: "INV-042", from: "Life Lab Ltd\n12 Grid Street", to: "Example University", due: "20 December 2026", items: $items, currency: GBP, tax: 20, notes: "Please pay within 30 days.")
}
```

`currency:` is a code such as `GBP`, `USD`, `EUR` or `JPY`, or any symbol. `tax:` is a percentage, `tax_name:` what it is called (default VAT), and `paid:` an amount already paid, which adds a balance due.

## Page and style

These go on `Document` and change what the template set:

| Attribute | What it does |
| --- | --- |
| `paper` | `a4` (default), `a3`, `a5`, `b5`, `letter`, `legal`, or a size such as `15cm x 20cm`. `landscape: true` turns it. |
| `margin` | One size (`2.5cm`), or `'2cm 3cm'` (top and bottom, sides), or four sizes like CSS. Sizes can be `mm`, `cm`, `in` or `pt`. |
| `font`, `heading_font` | `serif` (Times), `sans` (Helvetica) or `mono` (Courier). |
| `size`, `line_height` | The text size in points (`11`) and the line spacing as a multiple of it (`1.4`). |
| `align` | `justify`, `left`, `center` or `right`. |
| `accent` | The colour of headings and the title page: `#1f4e79` or a name such as `navy`. |
| `numbered` | `false` leaves sections without numbers. |
| `title_page`, `chapters` | `true` puts the title on its own page, and starts each top-level section on a new page. |
| `citations` | `numeric` or `author-year`. |
| `header`, `footer` | `'left \| centre \| right'`, with `[page]`, `[pages]`, `[title]`, `[author]` and `[date]`. `footer: "| Page [page] of [pages] |"` centres a page count. `footer: ""` has none. |
| `rules` | The default table lines: `booktabs`, `grid` or `none`. |
| `language` | The text's language, such as `en-GB`, for Word's spelling and HTML. |

## Fonts and characters

The PDF uses the fonts every PDF reader has (Times, Helvetica, Courier and Symbol), so nothing is embedded and files stay small. They cover Western European languages, Greek letters and maths symbols. Characters outside them, such as Chinese or emoji, come out as `?` in the PDF; the Word, HTML, Markdown and LaTeX files keep them.

## Commands

| Command | What it does |
| --- | --- |
| `@latex_escape(text)` | Text made safe to put in a LaTeX file: `\ { } $ & % # _ ~ ^` are escaped. |
| `@document_templates()` | The templates and what each is for. |
