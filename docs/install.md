# Installing KScript

Linux and macOS:

```sh
curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/kylepantall/kscript/master/install.ps1 | iex
```

The scripts pick the right build for your OS and CPU, install it to `~/.kscript/bin` (Windows: `%LOCALAPPDATA%\Programs\kscript`) and add that folder to your PATH. On macOS they also clear the quarantine flag, since the binary isn't notarized. Set `KSCRIPT_VERSION=v0.2.0` to install a specific release, `KSCRIPT_INSTALL` to choose the folder, or `KSCRIPT_NO_MODIFY_PATH=1` to leave your PATH alone.

To update later:

```sh
kscript update                    # install the latest release if it's newer
kscript update --check            # only report whether there is one
kscript update --version v0.2.0   # install a specific release
kscript update --no-ide           # leave the editor extension alone
kscript --version                 # the version you have
```

`kscript update` replaces the binary in place. On Windows the running `kscript.exe` is renamed to `kscript.exe.old` and deleted the next time kscript starts. It uses `GITHUB_TOKEN` if set, to avoid GitHub's rate limit.

Each release has a `SHA256SUMS` file listing the SHA-256 of every file in it. The install scripts, `kscript update` and [`kscript compile`](compile.md) (when it fetches kscript for another platform) check each download against that list before unpacking it, and install nothing when it doesn't match. Releases before 0.26 have no list, so installing one of those says the download wasn't checked. To check a download you made by hand, put `SHA256SUMS` beside it and run `sha256sum --check --ignore-missing SHA256SUMS` (macOS: `shasum -a 256 --check --ignore-missing SHA256SUMS`).

It also brings the [editor extension](#editor-support) up to the same release in every editor that already has it (VS Code, Insiders, Cursor, VSCodium, Windsurf), and tells you which ones it updated. Editors without the extension are left alone. This happens even when the binary is already current, so an extension that fell behind gets caught up. `--check` reports stale extensions too, and `--force` reinstalls them. Reload the editor window afterwards.

You can also download a build by hand from the [Releases page](https://github.com/kylepantall/kscript/releases): `linux-x64`, `linux-arm64`, `osx-arm64` (Apple Silicon), `osx-x64` and `win-x64`. Each `kscript-<platform>` archive holds `kscript`, a native program that needs no .NET, and the two libraries desktop windows draw with (`libSkiaSharp` and `libHarfBuzzSharp`). Keep the three files in the same folder; everything except windows works without the libraries. The `kscript-runtime-<platform>` archives are what [`kscript compile`](compile.md) builds programs from, and you don't need them to run kscript.

```sh
mkdir kscript && tar -xzf kscript-osx-arm64.tar.gz -C kscript
xattr -dr com.apple.quarantine kscript   # macOS only: the files aren't notarized
./kscript/kscript serve "Example Sites/team"
```

To build one yourself from source (Native AOT needs `clang` and `zlib1g-dev` on Linux, Xcode on macOS, or Visual Studio's C++ tools on Windows):

```sh
dotnet publish KScriptApp -c Release -r osx-arm64 -p:PublishAot=true -o out
```

Run `kscript docs` to open the documentation in your browser. It is built into `kscript`, so it works offline and matches the version you have. `kscript docs replace` opens it with a search for "replace".

Run a script with `kscript script.kscript [arguments]`. The arguments are in `$args`, a list. The exit code is 0, the code given to `exit`, or 3 when the script stops with an error (printed as `file:line:column: Type: message`); 2 means the command line itself was wrong ([exit codes](projects.md#exit-codes)). Ctrl+C stops a script; `<finally>` blocks still run.

## Editor support

Install the KScript extension into VS Code, Cursor, VSCodium or Windsurf:

```sh
kscript ide install           # every editor it finds; or name one: kscript ide install cursor
kscript ide                   # list the editors it found
kscript ide uninstall         # remove the extension again
```

The extension shows errors as you type and adds completion, hover, go to definition, rename and highlighting for scripts and `kscript serve` pages. It works out types as it goes: `$person.` and `$name->` offer the fields and methods of that value (including what your own functions return), inlay hints show the types you didn't write, and thin lines divide a script into blocks like a notebook. It runs `kscript lsp` from your installed `kscript`, so keep that current with `kscript update`. To use a different `kscript`, set `kscript.server.path` in the editor's settings.

Other editors with LSP support work too: `kscript ide config neovim` (or `helix`, `sublime`, `emacs`) prints the settings to add. `kscript check <folder>` reports the same problems on the command line, for CI. See [docs/language-server.md](language-server.md) for how the language server works.
