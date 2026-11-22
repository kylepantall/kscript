# AI assistants (MCP)

`kscript mcp` gives AI coding assistants these docs, so they write KScript from the reference of the version you have installed instead of guessing. It is a [Model Context Protocol](https://modelcontextprotocol.io) server: Claude Code, Claude Desktop, Cursor, VS Code, Windsurf, Codex, Gemini CLI and most other assistants can use it. It runs on your computer and works offline, and it is built from the same guides and language catalog as `kscript docs` and the editor, so all three always agree.

```sh
kscript mcp                 # the server on stdin and stdout: assistants start it themselves
kscript mcp --syntax xml    # show examples as XML instead of .kscript
kscript mcp --http          # serve on http://localhost:4181/mcp instead
```

For docs an assistant reads without a server, made from your own code, see [Coding agents](agents.md) (`kscript agents`).

You don't usually run it yourself. Add it to your assistant once, as below, and the assistant starts it when it needs it.

## What the assistant gets

Tools it can call:

| Tool | What it does |
|---|---|
| `search_docs` | Searches the guides and the reference: packages, elements, commands, methods, built-in variables and `kscript` commands. |
| `read_doc` | Reads a whole page as Markdown: a guide (`language`, `code-format`, `web`...), a package (`KScript.IO`), a type's methods (`string methods`), `variables` or `cli`. |
| `lookup` | Everything about one name: `<for>`, `@read_text`, `list->map`, `$args` or `KScript.Math`. Parameters and their types, the return type, retired v1 names and an example. |
| `list_make_commands` | The `kscript make` templates of your project (built in and `_templates/`) and the project templates. |
| `check_code` | Checks code or a file without running it, like `kscript check`, with your project's lint rules. |
| `format_code` | Formats code or a file like `kscript fmt`, with your project's format settings, and returns the result. |
| `convert_code` | Converts between XML and `.kscript`, like `kscript convert`. |
| `run_code` | Runs a short script in the same sandbox as the docs' Run buttons and returns what it printed. |

Every page is also a resource, such as `kscript://docs/guide/language` or `kscript://docs/reference/io`, for assistants that let you attach resources to a chat.

Examples come in your project's syntax: `make.syntax` in `kscript.json`, or `.kscript` outside a project. `--syntax` picks one for every call, and a tool call can ask for the other. The project is the folder the assistant starts the server in; `--dir` names another.

Nothing the server does changes your files. `check_code` and `format_code` read a file only when the assistant passes its path, and `run_code` runs in an empty temporary folder with no network.

## Setting it up

Install kscript first ([Install and editors](install.md)). Each assistant below starts `kscript mcp` itself. If one says it can't find `kscript`, which happens with desktop apps that don't read your shell's PATH, give the full path instead: `~/.kscript/bin/kscript` on macOS and Linux (written out, such as `/Users/you/.kscript/bin/kscript`), or `%LOCALAPPDATA%\Programs\kscript\kscript.exe` on Windows.

### Claude Code

```sh
claude mcp add kscript -- kscript mcp
```

Add `--scope project` to save it in the project's `.mcp.json`, so everyone working on the project gets it:

```json
{
  "mcpServers": {
    "kscript": { "command": "kscript", "args": ["mcp"] }
  }
}
```

### Claude Desktop

Open **Settings → Developer → Edit Config** and add the server to `claude_desktop_config.json`, then restart Claude:

```json
{
  "mcpServers": {
    "kscript": { "command": "/Users/you/.kscript/bin/kscript", "args": ["mcp"] }
  }
}
```

### Cursor

Add it to `.cursor/mcp.json` in your project, or to `~/.cursor/mcp.json` for every project:

```json
{
  "mcpServers": {
    "kscript": { "command": "kscript", "args": ["mcp"] }
  }
}
```

### VS Code

For GitHub Copilot's agent mode, add `.vscode/mcp.json` to your project (or run **MCP: Add Server** from the command palette):

```json
{
  "servers": {
    "kscript": { "type": "stdio", "command": "kscript", "args": ["mcp"] }
  }
}
```

### Windsurf

Add it to `~/.codeium/windsurf/mcp_config.json`:

```json
{
  "mcpServers": {
    "kscript": { "command": "kscript", "args": ["mcp"] }
  }
}
```

### Codex

```sh
codex mcp add kscript -- kscript mcp
```

Or add it to `~/.codex/config.toml`:

```toml
[mcp_servers.kscript]
command = "kscript"
args = ["mcp"]
```

### Gemini CLI

Add it to `~/.gemini/settings.json`, or `.gemini/settings.json` in your project:

```json
{
  "mcpServers": {
    "kscript": { "command": "kscript", "args": ["mcp"] }
  }
}
```

### Other assistants

Most assistants take the same thing: a command, `kscript`, with the argument `mcp`. For one that connects to a URL instead, run `kscript mcp --http` and give it `http://localhost:4181/mcp`. The HTTP server only listens on localhost and turns away requests from web pages. `--port` picks another port.

## Checking that it works

Ask the assistant something only these docs know, such as *"Which KScript command reads a JSON file, and what does it return?"*. It should call `lookup` or `search_docs` and answer with `@read_json`. Most assistants also list their servers: `claude mcp list`, **MCP** in Cursor's settings, or **MCP: List Servers** in VS Code.

To see what the server says without an assistant, send it a message yourself:

```sh
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | kscript mcp
```
