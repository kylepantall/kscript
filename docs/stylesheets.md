# Stylesheets: kscript css

A file ending in `.ks.css`, `.ks.scss` or `.ks.sass` is a stylesheet written in KScript. It is ordinary CSS (or Sass), with KScript elements such as `<let>`, `<for>` and `<if>` between the rules and `{$name}` or `{ @fn(...) }` in the text. `theme.ks.css` compiles to `theme.css`, `theme.ks.scss` to `theme.scss` and `theme.ks.sass` to `theme.sass`, so a theme kept in JSON becomes a stylesheet:

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
  color: {@contrast($theme.colors.primary)};   /* black or white, whichever reads better */
}
.button:hover { background: {@darken($theme.colors.primary, '10%')}; }
h1 { font-size: {@fluid(28, 48)}; }            /* 28px on phones, growing to 48px on wide screens */

@media {@up('md')} {
  .grid { display: grid; grid-template-columns: repeat(2, 1fr); }
}
<if test="$vars.dark ?? false">
@media (prefers-color-scheme: dark) { :root { --color-surface: #111; } }
</if>
```

- **Braces.** A `{` is KScript only when `$name`, `@name(` or `!` comes next. Every other brace is CSS, and Sass's own `#{$var}` is left alone. An element alone on its line leaves no blank line behind.
- **Screen widths.** `@up('md')` gives `(min-width: 768px)`, `@down('md')` gives `(max-width: 767.98px)`, and there is `@between('sm', 'lg')`. The names are sm 640, md 768, lg 1024, xl 1280 and xxl 1536, and `<let name="breakpoints" value="{ md: 800 }" />` sets your own. A number (`@up(900)`), `'900px'` or `'48em'` works too.
- **Sizes.** `@rem(24)` gives `1.5rem`. `@fluid(min, max, from?, to?)` gives a `clamp()` that grows from `min` px on screens up to `from` (default 640) to `max` px from `to` (default 1280).
- **Colours.** `@lighten`, `@darken`, `@mix(a, b, weight)`, `@alpha(colour, opacity)` and `@contrast(background, dark?, light?)` read `#rgb`, `#rrggbb`, `#rrggbbaa`, `rgb()`, `hsl()` and the common names. `@css_var('gap', '4px')` gives `var(--gap, 4px)`.
- **Building.** `kscript css` compiles every stylesheet in the current folder (or the files and folders you name), skipping `node_modules`, `.git` and partials whose names start with `_`. `--out public/css` writes the results to another folder, and `--var dark=true` sets `$vars.dark`. From a script, `<stylesheet src="theme.ks.css" to="public/theme.css" vars="{ dark: true }" />` and `@stylesheet(path, vars)` do the same. `$config` works in stylesheets too.
- **Serving.** `kscript serve` serves `theme.css` compiled from `theme.ks.css` on each request, unless a real `theme.css` is there, and never serves the `.ks.css` source itself. If a stylesheet fails, the error comes back as a CSS comment with status 500.
