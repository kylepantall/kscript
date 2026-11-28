# Stylesheets: kscript css

## Introduction

A stylesheet often repeats the same values: a brand colour and its lighter and darker shades, a spacing scale, the widths where a layout changes. KScript stylesheets let you work those out from data instead of copying them by hand.

A file ending in `.ks.css`, `.ks.scss` or `.ks.sass` is a stylesheet written in KScript. It is ordinary CSS or Sass with KScript expressions and control tags in it. `theme.ks.css` compiles to `theme.css`:

```css
<let name="brand" value="'#3355ff'" />
.button {
  background: {$brand};
  color: {@contrast_color($brand)};
  padding: {@rem(12)} {@rem(24)};
}
.button:hover { background: {@adjust_lightness($brand, '-10%')}; }
```

Stylesheets import `KScript.Style` for you, so its helpers, such as `@rem` and `@contrast_color`, are always there. Run `kscript docs KScript.Style` for the package reference.

## Writing stylesheets

### Expressions

Write a KScript expression in braces wherever CSS takes a value: `{$name}`, `{@rem(24)}`. A `{` is KScript only when `$name`, `@name(` or `!` comes next. Every other brace is CSS, and Sass's own `#{$var}` is left alone.

Inside a CSS string, a `<` is plain text, so `content: "<b>"` and an inline SVG in `url("data:image/svg+xml,<svg …>")` are left as written.

### Control tags

`<let>`, `<if>` and `<for>` work as they do in a script. Keep each on its own line between CSS rules. A tag alone on its line leaves no blank line behind:

```css
<let name="space" value="{ sm: 4, md: 8, lg: 16 }" />
<for each="name, px" in="$space">
.p-{$name} { padding: {@rem($px)}; }
</for>
<if $vars.dark ?? false>
@media (prefers-color-scheme: dark) { :root { --surface: #111; } }
</if>
```

### Values from data

A stylesheet may read its values from anywhere a script can:

- A variable: `{$name}`.
- A JSON file: `<let name="theme" value="@read_json('theme.json')" />`.
- Project configuration: `{$config.theme.accent}`.
- Build arguments: `{$vars.dark}`, set by `kscript css --var dark=true`.

Here a theme file drives the whole stylesheet:

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
  color: {@contrast_color($theme.colors.primary)};
}
```

## Sizes

`@rem()` turns pixels into rem. Its second argument sets the base, 16 unless you say:

```css
.card { padding: {@rem(24)}; }   /* 1.5rem */
```

`@fluid()` scales a value across screen widths: `@fluid(28, 48)` grows from 28px on a 640px screen to 48px on a 1280px one. Name the end points to choose other widths:

```css
h1 { font-size: {@fluid(28, 48)}; }
.hero { padding: {@fluid(16, 64, 'sm', 'xl')}; }
```

## Breakpoints

`@up()` starts a media query at a breakpoint, `@down()` ends one just below it, and `@between()` covers a range whose upper end is left out:

```css
@media {@up('md')} {
  .grid { display: grid; grid-template-columns: repeat(2, 1fr); }
}
@media {@down('md')} { .sidebar { display: none; } }
@media {@between('sm', 'lg')} { .grid { gap: 8px; } }
```

The breakpoints are `sm` 640, `md` 768, `lg` 1024, `xl` 1280 and `xxl` 1536, in pixels. You may use a number such as `@up(900)`, or a width such as `@up('48em')`, in place of a name.

To change a breakpoint or add one of your own, set `breakpoints` near the top of the stylesheet. The ones you don't name stay as they are:

```css
<let name="breakpoints" value="{ md: 800, wide: 1800 }" />
@media {@up('wide')} { .page { max-width: 1600px; } }
```

## Colours

Colour helpers take colour names, `#rgb`, `#rrggbb`, `#rrggbbaa`, `rgb()` and `hsl()`.

### Opacity and mixing

`@alpha()` sets a colour's opacity. `@mix()` blends two colours, half and half unless you give an amount:

```css
.overlay { background: {@alpha('#3355ff', 0.2)}; }
.tint { background: {@mix('#3355ff', 'white', 0.25)}; }   /* a quarter of the way to white */
```

### Lightness

`@adjust_lightness()` adds to a colour's HSL lightness, keeps it between 0% and 100%, and returns CSS text. A positive amount makes the colour lighter, and a negative one darker. `@lighten()` and `@darken()` do the same with a positive amount:

```css
.button:hover { background: {@adjust_lightness('#3355ff', '-10%')}; }
.button:active { background: {@darken('#3355ff', '20%')}; }
.badge { background: {@lighten('red', 0.5)}; }   /* #ffffff */
```

A native `color` value in a script is different. Its `->lighten()` and `->darken()` mix the colour's red, green and blue towards white or black, and return another `color`. With `brand: color = red`, `$brand->lighten(0.5)` gives `#ff8080`, halfway to white, while `@lighten('red', 0.5)` adds 50 points to red's 50% lightness and reaches white.

### Readable text

`@contrast_color()` picks black or white, whichever reads better on a colour. Give it your own dark and light colours as the second and third arguments:

```css
.button { color: {@contrast_color('#3355ff')}; }
.tag { color: {@contrast_color('#ffcc00', '#222', '#fafafa')}; }
```

## Custom properties

`@css_var()` reads a CSS custom property with a fallback. `@css_vars()` writes a custom property for every entry in a map, joining nested keys with dashes:

```css
<let name="theme" value="{ primary: '#3355ff', surface: '#ffffff' }" />
.stack { gap: {@css_var('gap', '4px')}; }   /* var(--gap, 4px) */
:root { { @css_vars($theme, 'theme') } }   /* primary: '#3355ff' becomes --theme-primary: #3355ff; */
```

Use these for a group of components that share a tint, border, blur or shadow: put the shared values in custom properties and override them where one differs. When a project needs groups of declarations with parameters, Sass mixins in a `.ks.scss` file can handle them.

## Building stylesheets

`kscript css` compiles every stylesheet in the current folder, or the files and folders you name. It skips `node_modules`, `.git` and partials whose names start with `_`:

```sh
kscript css                          # every stylesheet in this folder
kscript css --out public/css         # write the results to another folder
kscript css --var dark=true          # set $vars.dark
kscript make css site                # start a new site stylesheet
```

With `--out`, when two stylesheets would be written to the same file, nothing is built and the command says which.

A script may compile a stylesheet too. `@stylesheet()` returns the CSS as text, so you can write it wherever you like. Add `import KScript.Style` first:

```kscript
import KScript.IO
import KScript.Style

@write_text('public/theme.css', @stylesheet('theme.ks.css', { dark: true }))
```

### Serving stylesheets

`kscript serve` compiles `theme.ks.css` on each request for `theme.css`, unless a real `theme.css` is there, and never serves the `.ks.css` source itself. When a stylesheet fails, the error comes back as a CSS comment with status 500. The Team example site defines its styles in `Example Sites/team/site.ks.css`, and links to `/site.css` from its layout.

### Sass

A `.ks.scss` or `.ks.sass` stylesheet compiles to ordinary Sass, which then goes through your own Sass compiler. Run `kscript css` on the KScript source before the project's Sass compiler runs.
