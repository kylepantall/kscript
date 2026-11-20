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
kscript --version
```

`kscript update` replaces the binary in place. On Windows the running `kscript.exe` is renamed to `kscript.exe.old` and deleted the next time kscript starts. It uses `GITHUB_TOKEN` if set, to avoid GitHub's rate limit.

You can also download a build by hand from the [Releases page](https://github.com/kylepantall/kscript/releases): `linux-x64`, `linux-arm64`, `osx-arm64` (Apple Silicon), `osx-x64` and `win-x64`. Each is a single `kscript` file that includes the .NET runtime:

```sh
tar -xzf kscript-osx-arm64.tar.gz
xattr -d com.apple.quarantine kscript   # macOS only: the binary isn't notarized
./kscript serve "Example Sites/team"
```

To build one yourself from source:

```sh
dotnet publish KScriptApp -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
```

Run a script with `kscript script.ks.xml [arguments]`. The arguments are in `$args`, a list. The exit code is 0, the code given to `<exit code="..."/>`, or 3 when the script stops with an error (printed as `file:line:column: Type: message`). Ctrl+C stops a script; `<finally>` blocks still run.

## Editor support

Install the KScript extension into VS Code, Cursor, VSCodium or Windsurf:

```sh
kscript ide install           # every editor it finds; or name one: kscript ide install cursor
kscript ide                   # list the editors it found
kscript ide uninstall
```

The extension shows errors as you type and adds completion, hover, go to definition, rename and highlighting for scripts and `kscript serve` pages. It works out types as it goes: `$person.` and `$name->` offer the fields and methods of that value (including what your own functions return), inlay hints show the types you didn't write, and thin lines divide a script into blocks like a notebook. It runs `kscript lsp` from your installed `kscript`, so keep that current with `kscript update`. To use a different `kscript`, set `kscript.server.path` in the editor's settings.

Other editors with LSP support work too: `kscript ide config neovim` (or `helix`, `sublime`, `emacs`) prints the settings to add. `kscript check <folder>` reports the same problems on the command line, for CI. See [docs/language-server.md](language-server.md) for how the language server works.
