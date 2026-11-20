# Programs: kscript compile

`kscript compile` turns a script into a program that runs on computers without kscript or .NET. It works for all three kinds of KScript:

- a terminal script, which becomes a command-line program;
- a `<KScript.Window>` desktop app, which becomes a Mac `.app`, a Windows `.exe` with its icon, or a Linux program with a launcher entry;
- a `kscript serve` site, which becomes a program that serves it.

```sh
kscript compile hello.ks.xml                         # dist/linux-x64/hello on Linux, dist/mac-arm64/hello on an Apple Silicon Mac
kscript compile                                      # the project's app (the "app" section of kscript.json)
kscript compile --target all                         # every platform at once
kscript compile --target mac-arm64,win-x64           # some of them
kscript compile counter.ks.xml --name "Counter" --out build
```

Each program is one file. The exception is a Mac desktop app, which is an `.app` folder around one file. The program holds the script, the scripts it imports and every other file in its folder or project, such as data, pages and pictures, so `<import src>`, `@readFile` and pages work as they do with `kscript`. The arguments a program is started with are the script's `$args`.

## What a program holds

A program is the release `kscript` for its platform, reduced to what the script needs:

- **Only the packages it imports.** `kscript compile` loads the script and its imports the same way `kscript` does, and keeps the packages they use and the libraries those packages need. A terminal script that imports `KScript.Math` leaves out the desktop toolkit, the web server, the language server, the command line and every other package. Packages listed in `kscript.json` count as imported. A desktop app keeps `KScript.UI` and its toolkit. A site keeps `KScript.Http`, `KScript.Web` and the packages its pages import.
- **Your files.** Everything in the script's folder (or the project) goes in, except tests (`*.test.ks.xml`), `bin`, `obj`, `dist`, `node_modules`, `.git`, editor folders and earlier `.app` builds.
- **No secrets.** `.env` and `.env.*` files are left out, and `kscript compile` says so. A program you hand to someone shouldn't carry your keys. Set those values as environment variables where the program runs: `APP_` variables and the rest of [`$config`](language.md) work as usual, and `config.json` and `<env>.yaml` files do go in.

For a sense of size: on Linux, a terminal script is about 35 MB, Life Lab about 42 MB and a site about 46 MB, where the whole `kscript` is 60 MB. Most of what remains is .NET itself.

When a program starts for the first time, the .NET host unpacks its scripts and native libraries into a folder under `.net` in the home folder (`%TEMP%\.net` on Windows), named after the program and a hash of its contents. Later starts reuse that folder, so they take about as long as `kscript` itself, a fifth of a second for a small script. A changed program gets a new hash, so it never runs stale files.

## Targets

| Target | For | What you get |
| --- | --- | --- |
| `mac-arm64` | Macs with Apple Silicon | `hello`, or `Name.app` for a desktop app |
| `mac-x64` | Intel Macs | the same |
| `linux-x64` | Linux on x64 | `hello`, or `Name` plus `Name.desktop` and `Name.png` for a desktop app |
| `linux-arm64` | Linux on ARM, such as a Raspberry Pi 4 or 5 | the same |
| `win-x64` | Windows, on x64 and on ARM through emulation | `hello.exe`, or `Name.exe` for a desktop app |

`--target` takes one or more of these, separated by commas, or `all`. `mac`, `linux` and `windows` mean `mac-arm64`, `linux-x64` and `win-x64`. Without `--target`, the program is built for the computer you're on. Each target gets its own folder under `dist/` (or `--out`).

Programs are built from a runtime: the single-file build of `kscript` for the target platform, published with each release as `kscript-runtime-<platform>`. `kscript compile` downloads it once from the release that matches your `kscript --version`, keeps it in `~/.kscript/runtimes` (`%LOCALAPPDATA%\kscript\runtimes` on Windows), and reuses it after that, so only the first build for each platform needs the network. To work offline, or with your own build, pass `--runtime` with a runtime binary or archive (`kscript-runtime-win-x64.zip`) for the one target you're building.

A `kscript` built from the repository with `dotnet build` can only build programs with `--runtime`.

## Names, icons and versions

In a project, the [`app` section of `kscript.json`](projects.md#project-settings-kscriptjson) describes the program:

```jsonc
{
  "name": "life-lab",
  "app": {
    "main": "desktop.ks.xml",      // what kscript compile builds when you don't name a script
    "name": "Life Lab",            // the program's name: the .app, the .exe, the Dock and Task Manager
    "id": "app.kscript.lifelab",   // the Mac bundle id
    "version": "1.2.0",            // shown in Finder's Get Info and in the .exe's details
    "icon": "icon.png"             // one picture for every platform
  }
}
```

`--name` overrides `app.name`. Without either, a desktop app is named after the script's file (`life-lab.ks.xml` becomes `Life Lab`) and a terminal program keeps the file's name (`hello`). Spaces become `-` in file names except for Mac apps and Windows programs.

The icon can be a PNG, an `.ico` or an `.icns`. `kscript compile` puts it where each platform looks: inside the Windows `.exe` (so Explorer, the taskbar and shortcuts show it), as `AppIcon.icns` inside the Mac app, and as a PNG beside the Linux program. Pictures are carried over unchanged, not resized, so a 1024×1024 PNG gives a sharp icon everywhere. For the best Windows icon, use an `.ico` with several sizes up to 256×256.

Windows programs also get the app's name and version in their file details, which is what Task Manager shows. Desktop apps open without a console window on Windows; terminal programs keep theirs.

## On each platform

- **macOS.** Programs are signed ad hoc, which Apple Silicon needs before it will run them, even when they're built on Linux or Windows. On a Mac, `kscript compile` also seals the `.app` with Apple's `codesign`. Ad hoc signing isn't notarization: a program you download from the internet opens after right-clicking it and choosing **Open**. To publish an app outside your own Macs, sign it with your Developer ID and notarize it.
- **Windows.** `hello.exe` runs from a terminal. `Name.exe` for a desktop app opens a window when double-clicked. Programs aren't Authenticode-signed, so SmartScreen may ask before the first run of a downloaded one.
- **Linux.** Make sure the program is executable after copying it (`chmod +x`). To add a desktop app to your launcher, copy `Name.desktop` to `~/.local/share/applications`, `Name.png` to `~/.local/share/icons`, and the program to a folder on your `PATH` such as `~/.local/bin`.

## Sites

A folder (or project) with no `app.main` but with an `index.html`, `index.md`, `routes.ks.xml` or `api.ks.xml` compiles to a program that serves the site, as `kscript serve` does. It takes `--port` and `--host`, and otherwise uses the `serve` settings in `kscript.json`:

```sh
kscript compile "Example Sites/team"
./"Example Sites/team/dist/linux-x64/team" --port 8080
```

## And other languages

`--to` picks what `kscript compile` makes. `exe`, a program, is the default and the only choice so far. Compiling KScript to TypeScript and other languages is planned under the same command (`kscript compile lib/pricing.ks.xml --to ts`); see the [roadmap](roadmap.md).
