# Task board

A live page whose buttons are all handler shorthands, with tests that click through it.

- `_components/task-board.kscript` uses `toggle:`, `add: ... to:`, `remove: ... from:`, `increment:`, `set: ... to:` and `call: ... with:`, and `@class(...)` in both its forms. Its Finish buttons also send `task-finished` with `emit:`.
- `_components/finished-count.kscript` hears that event with `@on-event` and counts the finished tasks.
- `tests/board.test.kscript` opens the page with `@visit('/')`, then uses `->fill()`, `->submit()` and `->click()` to run each handler, and checks `.text`, `.html` and `->classes()`.

Run it with `kscript serve samples/task-board`, and test it with `kscript test samples/task-board`.
