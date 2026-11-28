# App folders: KScript.Storage

## Introduction

Most apps keep a few files of their own: settings, saved data, a cache, recent files and logs. Each operating system has a place for each of these, and the places differ from macOS to Windows to Linux. KScript.Storage finds the right one for you. You name the kind of folder, and your script reads and writes files in it without building any paths.

To get started, import the package and ask for a folder with `@storage()`:

```kscript
import KScript.Storage

settings = @storage(config)->read_json('settings.json', default: { theme: 'light' })
settings.theme = 'dark'
@storage(config)->write_json('settings.json', $settings)
```

[`samples/storage`](../samples/storage) is a small notes app that keeps its notes, a counter and the person's own settings this way.

## The folders

`@storage(kind)` gives one of the app's folders. The kind is `data` when you leave it out:

| Kind | For | macOS | Windows | Linux |
| --- | --- | --- | --- | --- |
| `config` | Settings | `~/Library/Application Support/<App>` | `%APPDATA%\<App>` | `$XDG_CONFIG_HOME/<app>` (`~/.config/<app>`) |
| `data` | The app's own files | `~/Library/Application Support/<App>` | `%LOCALAPPDATA%\<App>` | `$XDG_DATA_HOME/<app>` (`~/.local/share/<app>`) |
| `cache` | Files that can be made again | `~/Library/Caches/<App>` | `%LOCALAPPDATA%\<App>\Cache` | `$XDG_CACHE_HOME/<app>` (`~/.cache/<app>`) |
| `state` | History, recent files, window positions | `~/Library/Application Support/<App>` | `%LOCALAPPDATA%\<App>\State` | `$XDG_STATE_HOME/<app>` (`~/.local/state/<app>`) |
| `logs` | Log files | `~/Library/Logs/<App>` | `%LOCALAPPDATA%\<App>\Logs` | `$XDG_STATE_HOME/<app>/logs` |

On Windows, settings go in the roaming `AppData` folder, so they follow the person to another computer, and everything else stays in `LocalAppData`. macOS keeps settings, data and state together in Application Support, as Mac apps do. On Linux, the folder name is lower case with dashes, so `Life Lab` becomes `life-lab`, and the `XDG_*` variables are used when they hold an absolute path.

A folder is created the first time something is written to it. Reading from an app that hasn't saved anything yet gives you the default, not an error.

`@app_folders()` gives every folder as a map, which is handy for a "show me where my files are" command:

```kscript
import KScript.Storage

for kind, folder in @app_folders() {
  echo "{ $kind }: { $folder }"
}
```

### Which app the folders belong to

The folders are named after the app. KScript.Storage finds the name in this order:

1. The `app` argument: `@storage(data, app: 'Notes')`.
2. `app.name` in the nearest `kscript.json`, then its `name` (see [Projects](projects.md)).
3. The script's file name without its extension, so `notes.kscript` is `notes`.

`@app_name()` tells you the name a script gets. Characters that can't be in a folder name, such as `/` and `:`, become dashes.

## Reading and writing files

A folder has methods for the files in it. A file's name is its path inside the folder, with `/` between folders on every platform. A name that would leave the folder, such as `../other` or an absolute path, is refused.

### Reading files

`->read()` gives a file's text. When the file isn't there, you get `null`, or the default you give:

```kscript
import KScript.Storage

notes = @storage(data)->read('notes.txt', default: '')
echo $notes
```

`->read_json()` reads a file as JSON. `->exists()` tells you whether a file or folder is there, and with no name, whether the folder itself exists yet:

```kscript
import KScript.Storage

recent = @storage(state)->read_json('recent.json', default: [])
if not @storage(state)->exists('recent.json') {
  echo 'Nothing opened yet'
}
echo $recent
```

### Reading typed settings

Give `->read_json()` a type with `as:`, and it checks the file's shape and turns its objects into records of that type. A missing file gives you the default exactly as you wrote it, so make the default a record of the same type:

```kscript
import KScript.Storage

type Settings {
  theme: string
}

settings = @storage(config)->read_json('settings.json', default: Settings({ theme: 'light' }), as: Settings)
echo $settings.theme
```

### Writing files

`->write()` writes text to a file, and `->write_json()` writes a value as indented JSON. Both write beside the old file and then move the new one into place, so a crash never leaves half a file. `->append()` adds text to the end of a file:

```kscript
import KScript.Storage

@storage(data)->write('notes/today.txt', 'Buy milk')
@storage(data)->write_json('counter.json', { count: 1 })
@storage(logs)->append('app.log', "Started\n")
```

### Listing, deleting and clearing

`->list()` gives the names of the files, sorted, such as `['notes/a.txt', 'settings.json']`. Give it a wildcard such as `'*.json'` to list only some, and `recursive` to look inside folders too. `->delete()` deletes a file, or a folder and everything in it, and returns true if something was deleted. `->clear()` deletes everything in the folder and returns how many files there were:

```kscript
import KScript.Storage

echo @storage(data)->list(recursive)
@storage(cache)->delete('thumbnails')
echo "Cleared { @storage(cache)->clear() } files"
```

### Folders inside folders

`->folder()` gives a folder inside this one, with the same methods:

```kscript
import KScript.Storage

drafts = @storage(data)->folder('drafts')
$drafts->write('letter.md', '# Dear Joan')
for name in $drafts->list('*.md') {
  echo "{ $name }: { $drafts->read($name)->count() } characters"
}
$drafts->delete('letter.md')
```

A folder also has the fields `.app`, `.kind` and `.path` (its own full path), and it prints as its path. When you need more than these methods, `->path_of()` gives a file's full path to pass to KScript.IO.

## The person's own settings

`$config` reads the files listed in `kscript.json` under `config.files` (see [Settings](language.md#settings-config)). Turn on `config.user`, and it also reads `config.json`, `config.yaml` or `config.yml` from the app's config folder, after the project's own files:

```jsonc
// kscript.json
{
  "app": { "name": "Notes" },
  "config": { "user": true }
}
```

A person can then change a setting for themselves without editing the project, and a script can save one for them:

```kscript
import KScript.Storage

echo $config->get('greeting', 'Hello')
@storage(config)->write_json('config.json', { greeting: 'Hi there' })
echo $config->reload()->get('greeting')
```

The `config` statement does the same for one script: `config(user: true)`. Environment variables with the config prefix still win over every file.

## Where storage works

You may use KScript.Storage in scripts, desktop apps and the controllers of `kscript serve`. In a controller, the app's folders are the one place outside the site folder it can write to. Pages are read-only, so they can read from storage but not write.

The docs' "try it" boxes don't run KScript.Storage, because it writes outside the example's own folder.

### Moving the folders

Two environment variables move the folders, which tests, portable installs and containers use:

- [`KSCRIPT_STORAGE_ROOT`](/reference/environment#env-kscript-storage-root) moves every app's folders to `<root>/<app>/<kind>`, such as `<root>/notes/data`.
- [`KSCRIPT_PROJECT_DATA_ROOT`](/reference/environment#env-kscript-project-data-root) is the folder for relative auth and SQLite paths in `kscript.json`, instead of the project folder. A path that leaves it is an error.

Both need an absolute path. In Docker, put both on a volume, or users, sessions and stored files are lost when the container is recreated. The files `kscript make docker` writes do this for you (see [Where a container keeps its data](projects.md#where-a-container-keeps-its-data)).

KScript.Cache keeps its file cache in the `cache` folder (see [Caching](cache.md)).
