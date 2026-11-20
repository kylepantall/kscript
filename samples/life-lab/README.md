# Life Lab

Conway's Game of Life, written once in KScript and run three ways: in a terminal, in the browser, and as a desktop app on macOS, Windows or Linux. All three use the same rules script, [`lib/life.ks.xml`](lib/life.ks.xml), so a change there changes every version.

| File | What it is |
| --- | --- |
| `lib/life.ks.xml` | The shared logic: grids, the rules, ten built-in patterns, the `.cells` pattern format, worlds (generation, births, deaths, history) and spotting still lifes, oscillators and extinction. |
| `life.ks.xml` | The terminal version: an animated, coloured board and a menu. |
| `index.html`, `site.css` | The browser version, served by `kscript serve`. No JavaScript: every button runs KScript on the server and only the board is redrawn. |
| `desktop.ks.xml` | The desktop version: a native window with `<KScript.Window>`. `kscript app build` packages it as an app. |
| `kscript.json` | The project: the port `kscript serve` uses, and the app's name and main script for `kscript app build`. |
| `tests/life.test.ks.xml` | Tests for the rules and the terminal version, run by `kscript test`. |

Run the commands below from this folder (`samples/life-lab`). They need a `kscript` built from this repository, or a release that includes `KScript.UI` and `kscript app build`; to use the repository's build without installing it, replace `kscript` with `dotnet run --project ../../KScriptApp --`.

## In a terminal

```sh
kscript life.ks.xml                  # interactive: step, play, choose patterns, random soup, flip cells
kscript life.ks.xml glider_gun 120   # play a pattern for 120 generations, then stop
kscript life.ks.xml my.cells 200     # a pattern file in the plaintext .cells format
```

The patterns are `glider`, `blinker`, `toad`, `beacon`, `pulsar`, `spaceship`, `r_pentomino`, `acorn`, `diehard` and `glider_gun`. When the output isn't a terminal (a pipe, a file, a test), only the last frame is printed.

## In the browser

```sh
kscript serve .        # then open http://localhost:8080 (the port is in kscript.json)
```

Click cells to draw, then press **Play**. **Step**, **+10** and **+50** move on by hand, the pattern menu loads a pattern, **Random soup** fills the board, and **Wrap edges** joins the edges up like a torus.

The page imports the rules with `<import src="lib/life.ks.xml" />`, and **Play** uses an `on-tick` handler that runs every few hundred milliseconds while the game is playing.

## As a desktop app

```sh
kscript desktop.ks.xml     # opens Life Lab in a window
kscript app build          # packages it: dist/Life Lab.app on a Mac, dist/Life Lab/ on Windows and Linux
open "dist/Life Lab.app"   # macOS
```

`desktop.ks.xml` is an ordinary KScript file whose root element is `<KScript.Window>`. Its `<let>` elements hold the window's state, and the rest is the layout: `<Row>`, `<Grid>`, `<Button>`, `<Select>`, `<Slider>` and a `<Box>` for each cell. After every click the window is drawn again from the variables, the same way a `<live>` block on a page works. **Play** is a `<Timer when="$playing">`. There is no Swift, C# or shell script anywhere.

`kscript app build` reads the `app` section of `kscript.json` (the main script, the name and an optional icon) and copies this folder and `kscript` itself into the app. The app runs on computers without kscript when it is built with a release `kscript`. On a Mac it is signed ad hoc, not notarized, so it is meant for your own machine. If macOS won't open it, right-click it and choose **Open**.

## Tests

```sh
kscript test        # 24 tests: the rules, worlds, the .cells format and the terminal version
kscript check       # editor diagnostics for every file here
```
