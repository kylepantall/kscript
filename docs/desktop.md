# Desktop apps: KScript.UI

A script that imports KScript.UI and holds a `Window { }` block (`<KScript.Window>` in XML) opens a native window on macOS, Windows or Linux. There is nothing else to write: no Swift, C#, XAML or shell scripts.

```kscript
import KScript.UI

Window(title: "Counter", width: 320, height: 200) {
  count = 0

  Text(size: 32) "{$count}"
  Row {
    Button(variant: primary, on_click: count += 1) "Add one"
    Button(on_click: count = 0, disabled: $count == 0) "Reset"
  }
}
```

`kscript counter.kscript` opens it, and the script ends when the window is closed. It works like a `<live>` block on a page:

- `import KScript.UI` goes at the top of the file with the other imports. Variables and `fn` functions directly inside `Window` run once and hold the window's state.
- Everything else is the layout. After every `on_click`, `on_change`, `on_enter` or `on_tick`, the layout is drawn again from the variables. `if`, `for` and `{ }` work as they do anywhere else. Controls stay the same object from one draw to the next, so typing and focus aren't lost.
- A handler holds statements, or a single value: `on_click: $open` flips a bool, and `on_click: $save` calls the function `save`.
- `bind: name` ties a `TextBox`, `CheckBox`, `Select` or `Slider` to a state variable. `$event.value` holds the new value in `on_change`.
- `Timer(every: 100ms, when: $playing, on_tick: …)` runs on a timer while `when` is true.
- If a handler fails, the error is shown at the top of the window and the window carries on.
- `Window` can also go in the middle of a script. The script waits while the window is open, then carries on with the window's changes.

| Element | What it is |
| --- | --- |
| `Column(gap, align, scroll)`, `Row(gap, align, wrap)`, `Grid(columns, gap)` | Layout: top to bottom, side by side, or equal columns. |
| `Text(size, weight, color, wrap)` | Text. |
| `Button(on_click, variant: primary, disabled)` | A button. |
| `TextBox(bind, placeholder, on_enter, multiline)`, `CheckBox(bind)`, `Select(bind, options)`, `Slider(bind, min, max, step)` | Inputs, each tied to a variable. `options` is a list, a list of `{ label, value }` maps, or a map from label to value. |
| `Box(on_click, border)` | A rectangle: a cell, a card or a panel. It can hold other elements. |
| `Spacer(size)`, `Timer(every, when, on_tick)` | Space, and a timer. |

Every element takes `width`, `height`, `padding`, `margin`, `background`, `radius` and `tooltip`. Colours are names such as `teal` or `#rrggbb`. `Window` takes `title`, `width`, `height`, `theme` (`dark`, `light` or `system`), `background`, `padding`, `gap` and `resizable`.

## Building the app: kscript compile

```sh
kscript compile counter.kscript --name "Counter"      # for this computer
kscript compile counter.kscript --target all          # for every platform
```

This builds `dist/mac-arm64/Counter.app` for a Mac, `dist/win-x64/Counter.exe` for Windows (with the app's icon, and no console window), and `dist/linux-x64/Counter` with a `.desktop` file for Linux. Each one holds the script's folder (or its whole `kscript.json` project, so imports and data files come along) and only the parts of kscript the app uses, so it runs on computers without kscript or .NET. In a project, the [`app` section of `kscript.json`](projects.md#project-settings-kscriptjson) names the main script, the app's name, version and icon, so `kscript compile` needs no arguments. See [Programs](compile.md) for the targets, icons and signing.

`kscript make project my-app` starts a project with a desktop app already set up: `desktop.kscript` and the `app` section of `kscript.json` (leave them out with `--no-desktop`).

[`samples/life-lab/desktop.kscript`](../samples/life-lab/desktop.kscript) is a complete example: the Game of Life with a clickable board, a timer and controls, sharing its rules with a terminal and a browser version.
