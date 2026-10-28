<!--
-----------------------------------------------------------------------------
 Copyright (c) NoMercy Labs.

 This file is part of NomNomzBot. You may use, change and share it for free,
 for any noncommercial purpose, under the PolyForm Noncommercial License
 1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
 See LICENSE for details, and TRADEMARKS.md for the name and logo.

 SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-----------------------------------------------------------------------------
-->

# NomNomzBot Stream Deck plugins

An npm workspace holding two independently installable Elgato Stream Deck plugins, plus the
internal library they share:

- **[`music/`](music/README.md)** — `NomNomzBot Music`, Spotify/music control over the bot's own
  Automation API.
- **[`obs/`](obs/README.md)** — `NomNomzBot OBS`, OBS Studio control over the bot's own
  Automation API.
- **`shared/`** — `@nomnomzbot/streamdeck-shared`, the connection/pairing/token layer both
  plugins depend on. **Not itself installable** — it has no `.sdPlugin` folder and is never
  packaged or released; it exists purely so the auth code has one copy, not two forks.

Each plugin is versioned, tested, built, and released independently — see their own READMEs for
development and release instructions.

## Setup

From this directory (`tools/streamdeck/`):

```bash
npm install       # installs and links all three workspace packages
```

Then work from `music/`, `obs/`, or `shared/` as needed — each has its own `npm run <script>`
(`build`, `test`, `typecheck`, `check`), runnable either from inside that directory or from here
with `-w <package>` (e.g. `npm run test -w music`).
