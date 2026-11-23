# Isolink Connect for KScript

Use this library from a KScript program on the same Windows, macOS or Linux computer as Isolink. It opens the Isolink desktop consent window on first use and gives the program access to its own spaces and records.

```sh
kscript lib add isolink-connect
```

From a script next to the project's `lib` folder, import the installed entry file and pass a function to `with_session`. Use a stable app id so later runs can reopen the same space.

```kscript
import "lib/isolink-connect/index.kscript" as isolink

fn save_note(session: any) {
  space = @isolink.open_space($session, 'notes', 'My notes')
  title = @isolink.set_value(1, 'Buy coffee')
  note = @isolink.edit_record($session, $space.id, 'note', 'shopping', [$title])
  return $note.id
}

id = @isolink.with_session('com.example.notes', 'My Notes', @save_note)
echo $id
```

`with_session` returns the callback's result and closes the connection when the callback finishes, including when it throws. During the callback, `$session.me` identifies the current person. If Isolink issues a reconnect token, store `$session.token` in your app's private storage and pass it as `token:` on the next call. Isolink controls consent and revocation.

| Function | Result |
| --- | --- |
| `status(session)` | Host status with `running` and `reachable` flags |
| `list_spaces(session)`, `get_space(session, space)` | The app's spaces or one space by id |
| `open_space(session, key, name)` | The space for this stable key, created on first use |
| `list_records(session, space, type?)` | Records in the space, optionally filtered by type |
| `get_record(session, space, id)` | The record or `null` |
| `edit_record(session, space, type, id, edits)` | The created or updated record |
| `delete_record(session, space, id)` | Deletes the record and returns `null` |

Edits use integer field ids from 1 to 65535. Build them with `set_value(field, value)`, `add_to_counter(field, by)`, `add_to_set(field, items)` or `remove_from_set(field, items)`. Counter increments are positive integers. Sets take a list of items.

The library uses the desktop Connect v1 protocol on loopback and requests the standard app data grant. The wire format and consent model are described in the [Isolink Connect documentation](https://isolink.app/docs/connect.html#protocol).
