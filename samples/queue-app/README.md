# Queue app

Jobs with KScript.Queue: a welcome email, and an order that charges, ships and sends a receipt one after another.

```sh
kscript signup.kscript               # queue some jobs
kscript queue status                 # see what is waiting
kscript worker --stop-when-empty     # run them
kscript test                         # tests with @fake_queue()
```

See [Queues and jobs](../../docs/queues.md).
