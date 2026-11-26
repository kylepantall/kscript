# Stylesheets: kscript css

A file ending in `.ks.css`, `.ks.scss` or `.ks.sass` is a stylesheet written in KScript. It is ordinary CSS or Sass with KScript expressions and control tags. `theme.ks.css` compiles to `theme.css`. Sass sources compile to ordinary Sass and then go through your Sass compiler.

## Stylesheet cheatsheet

Stylesheets import `KScript.Style` automatically. Wrap a helper call in braces when you put it in a CSS declaration or media query.

| Use | Paste into a stylesheet |
| --- | --- |
| Convert pixels to rem | `padding: {@rem(24)};` gives `1.5rem`. The optional second argument sets the base, which defaults to 16. |
| Scale a value across screen widths | `font-size: {@fluid(28, 48)};` scales from 28 px at 640 px to 48 px at 1280 px. Pass `@fluid(28, 48, 'sm', 'lg')` to choose the endpoints. |
| Apply a minimum or maximum width | `@media {@up('md')}` starts at 768 px. `@media {@down('md')}` ends just below it. |
| Limit a rule to a range | `@media {@between('sm', 'lg')}` covers 640 px up to 1024 px. The upper bound is exclusive. |
| Set a colour's opacity | `background: {@alpha('#3355ff', 0.2)};` |
| Blend two colours | `background: {@mix('#3355ff', 'white', 0.25)};` moves a quarter of the way to white. Omit the amount for a 50/50 mix. |
| Lighten or darken a colour | `background: {@adjust_lightness('#3355ff', '-10%')};` subtracts 10 percentage points of HSL lightness. Use a positive amount with `@lighten(color, amount)` or `@darken(color, amount)`. |
| Choose readable text | `color: {@contrast_color('#3355ff')};` chooses black or white. Pass custom dark and light colours as the second and third arguments. |
| Read a CSS custom property with a fallback | `gap: {@css_var('gap', '4px')};` gives `var(--gap, 4px)`. |
| Generate CSS custom properties | `:root { { @css_vars($theme, 'theme') } }` turns a map entry such as `primary: '#3355ff'` into `--theme-primary: #3355ff;`. Nested keys are joined with hyphens. |

The default breakpoints are `sm` 640, `md` 768, `lg` 1024, `xl` 1280 and `xxl` 1536, in pixels. Use a number such as `@up(900)` or a width such as `@up('48em')` in place of a name. Set `<let name="breakpoints" value="{ md: 800, wide: 1800 }" />` near the top of a stylesheet to change a default or add a name of your own. The defaults you don't name stay as they are.

Colour helpers accept common colour names, `#rgb`, `#rrggbb`, `#rrggbbaa`, `rgb()` and `hsl()`.

| Get values from | Write |
| --- | --- |
| A variable | `{$name}` |
| A JSON file | `<let name="theme" value="@read_json('theme.json')" />` |
| Project configuration | `{$config.theme.accent}` |
| Build arguments | Read `$vars.dark` after running `kscript css --var dark=true`. |
| A KScript script | `@stylesheet('theme.ks.css', { dark: true })` compiles and returns the stylesheet as text. In a script, add `import KScript.Style` first. |

Keep `<let>`, `<if>` and `<for>` tags on their own lines between CSS rules. Inside a CSS string a `<` is plain text, so `content: "<b>"` and an inline SVG in `url("data:image/svg+xml,<svg …>")` are left as written. Run `kscript docs KScript.Style` for the package reference.

## Generate a stylesheet from JSON

```json
{ "colors": { "primary": "#3355ff", "surface": "#ffffff" }, "space": { "sm": 4, "md": 8, "lg": 16 } }
```

```css
<let name="theme" value="@read_json('theme.json')" />
:root {
  { @css_vars($theme.colors, 'color') }        /* --color-primary: #3355ff; --color-surface: #ffffff; */
}

<for each="name, px" in="$theme.space">
.p-{$name} { padding: {@rem($px)}; }
</for>

.button {
  background: var(--color-primary);
  color: {@contrast_color($theme.colors.primary)};   /* black or white, whichever reads better */
}
.button:hover { background: {@adjust_lightness($theme.colors.primary, '-10%')}; }
h1 { font-size: {@fluid(28, 48)}; }            /* 28px on phones, growing to 48px on wide screens */

@media {@up('md')} {
  .grid { display: grid; grid-template-columns: repeat(2, 1fr); }
}
<if $vars.dark ?? false>
@media (prefers-color-scheme: dark) { :root { --color-surface: #111; } }
</if>
```

- **Braces.** A `{` is KScript only when `$name`, `@name(` or `!` comes next. Every other brace is CSS, and Sass's own `#{$var}` is left alone. An element alone on its line leaves no blank line behind.
- **Building.** `kscript css` compiles every stylesheet in the current folder (or the files and folders you name), skipping `node_modules`, `.git` and partials whose names start with `_`. `--out public/css` writes the results to another folder (when two stylesheets would be written to the same file there, nothing is built and the command says which), and `--var dark=true` sets `$vars.dark`. From a script, `@stylesheet(path, vars)` returns the CSS: `@write_text('public/theme.css', @stylesheet('theme.ks.css', { dark: true }))`. `$config` works in stylesheets too.
- **Serving.** `kscript serve` serves `theme.css` compiled from `theme.ks.css` on each request, unless a real `theme.css` is there, and never serves the `.ks.css` source itself. If a stylesheet fails, the error comes back as a CSS comment with status 500.

The Team example site defines its styles in `Example Sites/team/site.ks.css` and links to `/site.css` from its layout. For a new site stylesheet, run `kscript make css` with a name. If the project uses Sass, run `kscript css` on the `.ks.scss` source before running the project's Sass compiler.

Use `.ks.css` to derive palettes, responsive sizes and theme variants from data. For a group of components that share a tint, border, blur or shadow, put the shared values in CSS custom properties and override them locally. When a project needs parameterized groups of declarations, Sass mixins can handle them in a `.ks.scss` workflow.

## Adjusting lightness

`@adjust_lightness` adds to the HSL lightness channel, clamps it to 0–100%, and returns CSS text. Use a positive amount for a lighter colour and a negative amount for a darker one. The existing stylesheet commands can be written with the explicit name and produce the same output.

| Existing command | Equivalent adjustment | CSS result |
| --- | --- | --- |
| `@lighten('red', 0.5)` | `@adjust_lightness('red', 0.5)` | `#ffffff` |
| `@darken('red', '10%')` | `@adjust_lightness('red', '-10%')` | `#cc0000` |

On a native `color`, `->lighten(amount)` mixes the RGB channels towards white and `->darken(amount)` mixes them towards black. Both return another `color`. With `brand: color = red`, `$brand->lighten(0.5)` gives `#ff8080`, halfway to white. The stylesheet command adds 50 percentage points to red's 50% HSL lightness, reaching white.
