## Mistakes are marked as you type

Misspell a name and it is underlined before you run anything:

```kscript
name = "Joan"
echo "Hello $nmae!"
```

> `$nmae` is not defined. Did you mean `$name`?

Put the cursor on the underlined word and press `Ctrl+.` (`Cmd+.` on a Mac) to pick the fix.
Every problem in the file is also listed in the **Problems** panel, and each code, such as `KS4001`,
is a link to what it means.
