# Installing KScript

## Introduction

KScript is one program, `kscript`, that runs scripts, serves sites, checks code and builds apps. It is a native program, so it doesn't need .NET or anything else installed first. An install script picks the right build for your computer and puts it on your PATH.

## Installing

On Linux and macOS, run:

```sh
curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | sh
```

On Windows, run this in PowerShell:

```powershell
irm https://raw.githubusercontent.com/kylepantall/kscript/master/install.ps1 | iex
```

The scripts pick the build for your operating system and CPU and install it to `~/.kscript/bin` (on Windows, `%LOCALAPPDATA%\Programs\kscript`). They then add that folder to your PATH. On macOS they also clear the quarantine flag, since the binary isn't notarized.

Then check it worked:

```sh
kscript --version   # the version you have
```

### Install options

The install scripts read a few [environment variables](/reference/environment#installing-and-updating):

- `KSCRIPT_VERSION=v0.34.0` installs a specific release instead of the latest.
- `KSCRIPT_INSTALL` chooses the folder.
- `KSCRIPT_NO_MODIFY_PATH=1` leaves your PATH alone.

```sh
curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | KSCRIPT_VERSION=v0.34.0 sh
```

## Updating

`kscript update` installs the latest release when it is newer than yours:

```sh
kscript update                    # install the latest release if it's newer
kscript update --check            # only report whether there is one
kscript update --version v0.34.0  # install a specific release
kscript update --no-ide           # leave the editor extension alone
```

It replaces the binary in place. On Windows, the running `kscript.exe` is renamed to `kscript.exe.old`, and deleted the next time kscript starts. Set `GITHUB_TOKEN` to avoid GitHub's rate limit, such as on a shared CI runner.

`kscript update` also brings the [editor extension](#editor-support) up to the same release in every editor that already has it (VS Code, Insiders, Cursor, VSCodium and Windsurf), and tells you which ones it updated. Editors without the extension are left alone. This happens even when the binary is already current, so an extension that fell behind is caught up. `--check` reports stale extensions too, and `--force` reinstalls them. Reload the editor window afterwards.

## Checking downloads

Each release has a `SHA256SUMS` file that lists the SHA-256 of every file in it. The install scripts, `kscript update` and [`kscript compile`](compile.md) (when it fetches kscript for another platform) check each download against that list before unpacking it, and install nothing when it doesn't match. Releases before 0.26 have no list, so installing one of those says the download wasn't checked.

To check a download you made by hand, put `SHA256SUMS` beside it and run:

```sh
sha256sum --check --ignore-missing SHA256SUMS            # Linux
shasum -a 256 --check --ignore-missing SHA256SUMS        # macOS
```

## Installing by hand

You may also download a build from the [Releases page](https://github.com/kylepantall/kscript/releases). There are builds for `linux-x64`, `linux-arm64`, `osx-arm64` (Apple Silicon), `osx-x64` and `win-x64`.

Each `kscript-<platform>` archive holds `kscript` and the two libraries desktop windows draw with (`libSkiaSharp` and `libHarfBuzzSharp`). Keep the three files in the same folder. Everything except windows works without the libraries:

```sh
mkdir kscript && tar -xzf kscript-osx-arm64.tar.gz -C kscript   # unpack the archive
xattr -dr com.apple.quarantine kscript                          # macOS only: the files aren't notarized
./kscript/kscript --version                                     # check it runs
```

The `kscript-runtime-<platform>` archives are what [`kscript compile`](compile.md) builds programs from. You don't need them to run kscript.

### Building from source

To build kscript yourself, you need the .NET 8 SDK and a C++ toolchain for Native AOT: `clang` and `zlib1g-dev` on Linux, Xcode on macOS, or Visual Studio's C++ tools on Windows. Then publish for your platform:

```sh
dotnet publish KScriptApp -c Release -r osx-arm64 -p:PublishAot=true -o out
```

## Running scripts

Run a script with `kscript` and its file name. Anything after the name is in `$args`, a list:

```sh
kscript hello.kscript          # run a script
kscript greet.kscript Joan     # $args is ['Joan']
```

The exit code is 0, the code given to `exit`, or 3 when the script stops with an error, printed as `file:line:column: Type: message`. 2 means the command line itself was wrong (see [exit codes](projects.md#exit-codes)). Ctrl+C stops a script, and its `finally` blocks still run.

## Reading the docs

`kscript docs` opens these docs in your browser. They are built into `kscript`, so they work offline and match the version you have:

```sh
kscript docs           # open the docs
kscript docs replace   # open them with a search for "replace"
```

## Editor support

The KScript extension adds errors as you type, completion, hover, go to definition, rename and highlighting for scripts and `kscript serve` pages. Install it into VS Code, Cursor, VSCodium or Windsurf:

```sh
kscript ide install          # every editor it finds
kscript ide install cursor   # just one
kscript ide                  # list the editors it found
kscript ide uninstall        # remove the extension again
```

The extension works out types as it goes. `$person.` and `$name->` offer the fields and methods of that value, including what your own functions return. Inlay hints show the types you didn't write, and thin lines divide a script into blocks like a notebook.

It runs `kscript lsp` from your installed `kscript`, so keep that current with `kscript update`. To use a different `kscript`, set `kscript.server.path` in the editor's settings. Every command and setting of the extension is listed in [VS Code commands and settings](/reference/editor).

### Other editors

Other editors with LSP support work too. `kscript ide config` prints the settings to add:

```sh
kscript ide config neovim    # also helix, sublime or emacs
```

`kscript check <folder>` reports the same problems on the command line, for CI. See [the language server](language-server.md) for how it works.
