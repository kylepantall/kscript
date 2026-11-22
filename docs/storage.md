# App folders: KScript.Storage

Most apps keep a few files of their own: settings, saved data, a cache, recent files, logs. Each operating system has a place for each of these, and KScript.Storage finds it for you. You name the kind of folder, and your script reads and writes files in it without building any paths.

```kscript
import KScript.Storage

settings = @storage(config)->read_json('settings.json', default: { theme: 'light' })
settings.theme = 'dark'
@storage(config)->write_json('settings.json', $settings)

@storage(data)->write('notes/today.txt', 'Buy milk')
echo @storage(data)->list(recursive)
```

[`samples/storage`](../samples/storage) is a small notes app that keeps its notes, a counter and the person's own settings this way.

## The folders

`@storage(kind)` gives one of the app's folders. The kind is `data` when you leave it out.

| Kind | For | macOS | Windows | Linux |
| --- | --- | --- | --- | --- |
| `config` | Settings | `~/Library/Application Support/<App>` | `%APPDATA%\<App>` | `$XDG_CONFIG_HOME/<app>` (`~/.config/<app>`) |
| `data` | The app's own files | `~/Library/Application Support/<App>` | `%LOCALAPPDATA%\<App>` | `$XDG_DATA_HOME/<app>` (`~/.local/share/<app>`) |
| `cache` | Files that can be made again | `~/Library/Caches/<App>` | `%LOCALAPPDATA%\<App>\Cache` | `$XDG_CACHE_HOME/<app>` (`~/.cache/<app>`) |
| `state` | History, recent files, window positions | `~/Library/Application Support/<App>` | `%LOCALAPPDATA%\<App>\State` | `$XDG_STATE_HOME/<app>` (`~/.local/state/<app>`) |
| `logs` | Log files | `~/Library/Logs/<App>` | `%LOCALAPPDATA%\<App>\Logs` | `$XDG_STATE_HOME/<app>/logs` |

- On Windows, settings go in the roaming `AppData` folder so they follow the person to another computer. Everything else stays in `LocalAppData`.
- On Linux the folder name is lower case with dashes, so `Life Lab` becomes `life-lab`. The `XDG_*` variables are used when they hold an absolute path.
- macOS keeps settings, data and state together in Application Support, as Mac apps do.
- Folders are created the first time something is written to them, so reading from an app that has saved nothing yet gives the default instead of an error.

`@app_folders()` gives every folder as a map, which is handy for a "show me where my files are" command:

```kscript
import KScript.Storage

for kind, folder in @app_folders() {
  echo "$kind: $folder"
}
```

## Which app

The folders are named after the app, which is found in this order:

1. The `app` argument: `@storage(data, app: 'Notes')`.
2. `app.name` in the nearest `kscript.json`, then its `name` (see [Projects](projects.md)).
3. The script's file name without its extension, so `notes.kscript` is `notes`.

`@app_name()` tells you the name a script gets. Characters that can't be in a folder name, such as `/` and `:`, become dashes.

## Reading and writing

A folder value has these methods. Names are paths inside the folder, with `/` between folders on every platform. A name that would leave the folder, such as `../other` or an absolute path, is refused.

| Method | What it does |
| --- | --- |
| `->read(name, default: …)` | The file's text, or the default (`null`) when the file isn't there. |
| `->read_json(name, default: …)` | The file read as JSON, or the default. |
| `->write(name, text)` | Writes the file. It is written beside the old one and then moved into place, so a crash never leaves half a file. |
| `->write_json(name, value)` | Writes the value as indented JSON, the same safe way. |
| `->append(name, text)` | Adds text to the end of the file. |
| `->exists(name)` | True if the file or folder is there. Without a name, whether the folder itself exists yet. |
| `->delete(name)` | Deletes a file, or a folder and everything in it. True if something was deleted. |
| `->list(pattern, recursive)` | The names of the files, sorted, such as `['notes/a.txt', 'settings.json']`. `pattern` is a wildcard like `'*.json'`. |
| `->folder(name)` | A folder inside this one, with the same methods. |
| `->path(name)` | The full path of a file, or of the folder without a name. Give it to KScript.IO when you need more. |
| `->clear()` | Deletes everything in the folder and returns how many files there were. |

The value also has `.app`, `.kind` and `.path`, and it prints as its path.

```kscript
import KScript.Storage

drafts = @storage(data)->folder('drafts')
$drafts->write('letter.md', '# Dear Joan')
for name in $drafts->list('*.md') {
  echo "$name: { $drafts->read($name)->length() } characters"
}
$drafts->delete('letter.md')
```

## The person's own settings: config.user

`$config` reads a project's settings from `config.json`, `config.yaml` and `.env` (see [Settings](language.md#settings-config)). With `"user": true`, it also reads `config.json`, `config.yaml` or `config.yml` from the app's config folder, after the project's own files. So a person can change a setting for themselves without editing the project, and a script can save one for them:

```jsonc
// kscript.json
{
  "app": { "name": "Notes" },
  "config": { "user": true }
}
```

```kscript
import KScript.Storage

echo $config->get('greeting', 'Hello')
@storage(config)->write_json('config.json', { greeting: 'Hi there' })
echo $config->reload()->get('greeting')
```

The `config` statement does the same for one script: `config(user: yes)`. Environment variables with the config prefix still win over every file.

## Where it works

- Storage works in scripts, desktop apps, and the controllers of `kscript serve` (where it is the one place outside the site folder a controller can write, and only inside the app's folders).
- Pages are read-only, so they can read from storage but not write.
- `KSCRIPT_STORAGE_ROOT` moves every app's folders to `<root>/<app>/<kind>`. Tests use it to keep their files apart, and it makes a portable install that keeps everything next to the program.
- The docs' "try it" boxes don't run KScript.Storage, because it writes outside the example's own folder.

KScript.Cache keeps its file cache in the `cache` folder (see [Caching](cache.md)).
