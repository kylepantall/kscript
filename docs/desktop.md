# Desktop apps: KScript.UI

## Introduction

KScript.UI lets a script open a native window on macOS, Windows or Linux. You describe the window's layout and its state in the script, and KScript draws it. There is nothing else to write: no Swift, C#, XAML or shell scripts.

To get started, import the package and write a `Window { }` block (`<KScript.Window>` in XML):

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

`kscript counter.kscript` opens the window, and the script ends when the window is closed. A window needs a desktop: on a server, or over SSH with no display, the script stops with a `UI` error.

[`samples/life-lab/desktop.kscript`](../samples/life-lab/desktop.kscript) is a complete example: the Game of Life with a clickable board, a timer and controls, sharing its rules with a terminal and a browser version.

## How a window works

A window works like a `<live>` block on a page. Variables and `fn` functions directly inside `Window` run once, and hold the window's state. Everything else is the layout.

After every handler runs, KScript draws the layout again from the variables. `if`, `for` and `{ }` work as they do anywhere else. Controls stay the same object from one draw to the next, so typing and focus aren't lost:

```kscript
import KScript.UI

Window(title: "Shopping") {
  items = ['Milk', 'Bread']
  item = ''

  Column(gap: 8) {
    for name in $items {
      Text "{$name}"
    }
    if $items->count() == 0 {
      Text(color: "gray") "Nothing to buy"
    }
    TextBox(bind: $item, placeholder: "Add an item", on_enter: items = $items->push($item))
  }
}
```

`Window` can also go in the middle of a script. The script waits while the window is open, then carries on with the window's changes.

## Handling events

A handler holds statements, or a function to call. `on_click: $save` calls the function `save`:

```kscript
import KScript.UI

Window(title: "Notes") {
  saved = 0

  fn save() {
    saved += 1
  }

  Button(on_click: $save) "Save"
  Text "Saved {$saved} times"
}
```

A button or box also takes the [handler shorthands](components.md#handler-shorthands) directly, such as `Button(toggle: $open) "Details"`. `@close_window()` in a handler closes the window.

If a handler fails, the error is shown at the top of the window and the window carries on.

### Timers

`Timer` runs its `on_tick` handler on a timer, while `when` is true:

```kscript
import KScript.UI

Window(title: "Stopwatch") {
  seconds = 0
  running = false

  Timer(every: 1s, when: $running, on_tick: seconds += 1)
  Text(size: 32) "{$seconds}s"
  Button(toggle: $running) "Start or stop"
}
```

## Inputs

`bind: $name` ties a `TextBox`, `CheckBox`, `Select` or `Slider` to a state variable. When the person changes the input, the variable changes and the window is drawn again. In `on_change`, `$event.value` holds the new value:

```kscript
import KScript.UI

Window(title: "Settings") {
  name = ''
  dark = false
  size = 14
  theme = 'teal'

  Column(gap: 8) {
    TextBox(bind: $name, placeholder: "Your name")
    CheckBox(bind: $dark) "Dark mode"
    Slider(bind: $size, min: 10, max: 24, step: 1)
    Select(bind: $theme, options: ['teal', 'orange', 'purple'])
  }
}
```

`Select` takes its `options` as a list, a list of `{ label, value }` maps, or a map from label to value.

A `TextBox` takes the same binding options as `@bind` on a page. `on:` is `input` (as you type, the default), `change` or `blur` (when focus leaves or Enter is pressed). `trim: true` trims spaces, and `as:` (`string`, `int`, `number` or `bool`) reads the value as that type and shows an error for one that isn't:

```kscript
import KScript.UI

Window(title: "Age") {
  age = 0

  TextBox(bind: $age, on: blur, trim: true, as: int)
  Text "Next year you'll be {$age + 1}"
}
```

`multiline: true` makes a `TextBox` hold several lines, and `on_enter` runs when Enter is pressed.

## Layout

`Column` lays its contents out top to bottom, `Row` side by side, and `Grid` in equal columns. `Column` takes `gap`, `align` and `scroll`, `Row` takes `gap`, `align` and `wrap`, and `Grid` takes `columns` and `gap`. `Box` is a rectangle, such as a cell, a card or a panel, that can hold other elements and take `on_click` and `border`. `Spacer(size)` adds space:

```kscript
import KScript.UI

Window(title: "Board", padding: 16) {
  Grid(columns: 3, gap: 4) {
    for n in 1..9 {
      Box(border: "gray", padding: 12) {
        Text(text_align: center) "{$n}"
      }
    }
  }
  Spacer(size: 12)
  Row(gap: 8) {
    Button(variant: primary) "Play"
    Button "Reset"
  }
}
```

`Text` takes `size`, `weight`, `color`, `wrap` and `text_align`. `Button` takes `on_click`, `variant: primary` and `disabled`.

### Styling

Every element except `Spacer` and `Timer` takes `width`, `height`, `padding`, `margin`, `background`, `radius` and `tooltip`. Colours are names such as `"teal"`, or `"#rrggbb"`.

`Window` takes `title`, `width`, `height`, `theme` (`dark`, `light` or `system`), `background`, `padding`, `gap` and `resizable`.

### Components

A [component](components.md#components-in-windows-and-the-terminal) with a window `render` is drawn by writing it as a statement, such as `NoteCard(title: "Milk")`. Each one keeps its own state.

## Building the app

`kscript compile` turns a window script into an app that runs on computers without kscript or .NET:

```sh
kscript compile counter.kscript --name "Counter"   # for this computer
kscript compile counter.kscript --target all       # for every platform
```

This builds `dist/mac-arm64/Counter.app` for a Mac, `dist/win-x64/Counter.exe` for Windows (with the app's icon, and no console window), and `dist/linux-x64/Counter` with a `.desktop` file for Linux. Each one holds the script's folder, or its whole `kscript.json` project so imports and data files come along, and only the parts of kscript the app uses.

In a project, the [`app` section of `kscript.json`](projects.md#project-settings-kscriptjson) names the main script and the app's name, version and icon, so `kscript compile` needs no arguments. See [Programs](compile.md) for the targets, icons and signing.

`kscript make project my-app` starts a project with a desktop app already set up: `desktop.kscript` and the `app` section of `kscript.json`. Leave them out with `--no-desktop`.
