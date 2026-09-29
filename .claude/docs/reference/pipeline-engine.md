# Pipeline Engine

Commands and event responses use a visual pipeline system. Each pipeline is a list of **actions** with optional **conditions**.

**Built-in actions:** `SendMessage`, `SendReply`, `Timeout`, `Ban`, `Shoutout`, `SetVariable`, `Wait`, `PlayMusic`, `Stop`, and more.

**Conditions:** `UserRole` (broadcaster/mod/sub/vip/everyone), `Random` (percentage), variable comparisons.

**Template variables** (90+): `{{user.name}}`, `{{channel.title}}`, `{{stream.uptime}}`, `{{args.1}}`, `{{random.number:1:100}}`, etc.

All action blocks are compiled C# classes — no scripting engine.
