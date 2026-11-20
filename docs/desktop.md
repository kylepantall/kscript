# Desktop apps: KScript.UI

A script whose root element is `<KScript.Window>` opens a native window on macOS, Windows or Linux. There is nothing else to write: no Swift, C#, XAML or shell scripts.

```xml
<KScript.Window title="Counter" width="320" height="200">
  <let name="count" value="0" />

  <Text size="32">{$count}</Text>
  <Row>
    <Button variant="primary" on-click="count += 1">Add one</Button>
    <Button on-click="count = 0" disabled="$count eq 0">Reset</Button>
  </Row>
</KScript.Window>
```

`kscript counter.ks.xml` opens it, and the script ends when the window is closed. It works like a `<live>` block on a page:

- The `<let>`, `<fn>` and `<import>` elements directly inside `<KScript.Window>` run once and hold the window's state.
- Everything else is the layout. After every `on-click`, `on-change`, `on-enter` or `on-tick`, the layout is drawn again from the variables. `<if>`, `<for>` and `{ }` work as they do anywhere else. Controls stay the same object from one draw to the next, so typing and focus aren't lost.
- `bind="name"` ties a `<TextBox>`, `<CheckBox>`, `<Select>` or `<Slider>` to a state variable. `$event.value` holds the new value in `on-change`.
- `<Timer every="100" when="$playing" on-tick="...">` runs on a timer while `when` is true.
- If a handler fails, the error is shown at the top of the window and the window carries on.
- `<KScript.Window>` can also go inside a `<kscript>` script. The script waits while the window is open, then carries on with the window's changes.

| Element | What it is |
| --- | --- |
| `<Column gap align scroll>`, `<Row gap align wrap>`, `<Grid columns gap>` | Layout: top to bottom, side by side, or equal columns. |
| `<Text size weight color wrap>` | Text. |
| `<Button on-click variant="primary" disabled>` | A button. |
| `<TextBox bind placeholder on-enter multiline>`, `<CheckBox bind>`, `<Select bind options>`, `<Slider bind min max step>` | Inputs, each tied to a variable. `options` is a list, a list of `{ label, value }` maps, or a map from label to value. |
| `<Box on-click border>` | A rectangle: a cell, a card or a panel. It can hold other elements. |
| `<Spacer size>`, `<Timer every when on-tick>` | Space, and a timer. |

Every element takes `width`, `height`, `padding`, `margin`, `background`, `radius` and `tooltip`. Colours are names such as `teal` or `#rrggbb`. `<KScript.Window>` takes `title`, `width`, `height`, `theme` (`dark`, `light` or `system`), `background`, `padding`, `gap` and `resizable`.

## Packaging an app: kscript app build

```sh
kscript app build counter.ks.xml --name "Counter"
```

This builds `dist/Counter.app` on a Mac, and `dist/Counter/` with `Counter.exe` on Windows or a launcher and a `.desktop` file on Linux. It builds for the computer it runs on. The app holds the script's folder (or its whole `kscript.json` project, so imports and data files come along) and its own copy of `kscript`. Built with a release `kscript`, it runs on computers that don't have kscript or .NET. In a project, the [`app` section of `kscript.json`](projects.md#project-settings-kscriptjson) names the main script, the app's name and its icon, so `kscript app build` needs no arguments. Mac apps are signed ad hoc, not notarized: macOS opens them on the Mac that built them, and elsewhere after right-clicking and choosing **Open**.

`kscript make project my-app` starts a project with a desktop app already set up: `desktop.ks.xml` and the `app` section of `kscript.json` (leave them out with `--no-desktop`).

[`samples/life-lab/desktop.ks.xml`](../samples/life-lab/desktop.ks.xml) is a complete example: the Game of Life with a clickable board, a timer and controls, sharing its rules with a terminal and a browser version.
