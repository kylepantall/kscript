# Life Lab

Conway's Game of Life, written once in KScript and run three ways: in a terminal, in the browser, and as a Mac app. All three use the same rules script, [`lib/life.ks.xml`](lib/life.ks.xml), so a change there changes every version.

| File | What it is |
| --- | --- |
| `lib/life.ks.xml` | The shared logic: grids, the rules, ten built-in patterns, the `.cells` pattern format, worlds (generation, births, deaths, history) and spotting still lifes, oscillators and extinction. |
| `life.ks.xml` | The terminal version: an animated, coloured board and a menu. |
| `index.html`, `site.css` | The browser version, served by `kscript serve`. No JavaScript: every button runs KScript on the server and only the board is redrawn. |
| `mac/` | `make-app.sh` builds `Life Lab.app`, a native macOS window around the browser version with `kscript` inside. |
| `tests/life.test.ks.xml` | Tests for the rules and the terminal version, run by `kscript test`. |

Run the commands below from this folder (`samples/life-lab`). They need a `kscript` built from this repository, or a release that includes page imports and `on-tick`; to use the repository's build without installing it, replace `kscript` with `dotnet run --project ../../KScriptApp --`.

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

## On a Mac

```sh
cd mac
./make-app.sh                 # builds kscript from this repository and bundles it (needs the .NET 8 SDK)
open "Life Lab.app"
```

`make-app.sh --kscript ~/.kscript/bin/kscript` bundles an installed `kscript` instead of building one, and `--out ~/Applications` puts the app there. It needs the Xcode command line tools (`xcode-select --install`) for `swiftc`.

The app is a small Swift program (`mac/LifeLab.swift`) that starts the bundled `kscript serve` on a free local port and shows the page in a native window; quitting the app stops the server. The app is signed ad hoc, not notarized, so it is meant for your own Mac.

Native KScript windows (`KScript.UI`, with Avalonia) aren't built yet. Once they are, the Mac version can be a KScript script too, using the same `lib/life.ks.xml`.

## Tests

```sh
kscript test        # 24 tests: the rules, worlds, the .cells format and the terminal version
kscript check       # editor diagnostics for every file here
```
