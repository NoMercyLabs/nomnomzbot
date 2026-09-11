<!--
-----------------------------------------------------------------------------
 Copyright (c) NoMercy Labs.

 This file is part of NomNomzBot, free software licensed under the GNU Affero
 General Public License v3.0 or later. You may redistribute and/or modify it
 under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

 SPDX-License-Identifier: AGPL-3.0-or-later
-----------------------------------------------------------------------------
-->

# NomNomzBot OBS — Stream Deck plugin

Control OBS Studio through your bot's own Automation API. Pairs itself, defaults to your
self-hosted bot at `localhost`.

## Install

Download the latest `bot.nomnomzbot.streamdeck.obs.streamDeckPlugin` from the
[Releases page](https://github.com/NoMercyLabs/nomnomzbot/releases?q=streamdeck-obs-v) and
double-click it — the Stream Deck app installs it directly. Requires Stream Deck software 6.5+.

One package installs identically on **Windows** and **macOS**; a `.streamDeckPlugin` file is just
the `.sdPlugin` folder zipped with that extension, so there's no separate installer per OS.

**Linux** is not supported — Elgato does not ship an official Stream Deck app for Linux. Community
projects such as [`streamdeck-linux-gui`](https://github.com/streamdeck-linux-gui/streamdeck-linux-gui)
reimplement a Stream Deck client and may load third-party plugins, but they're unofficial, not built
or tested by NoMercy Labs, and compatibility isn't guaranteed.

## Development

This package lives in an npm workspace ([`tools/streamdeck/`](../README.md)) alongside the Music
plugin and the `@nomnomzbot/streamdeck-shared` connection layer both depend on. Install once from
the workspace root, then work from here:

```bash
cd tools/streamdeck && npm ci   # once, from the workspace root
cd obs
npm run build       # bundles src/plugin.ts -> bot.nomnomzbot.streamdeck.obs.sdPlugin/bin/plugin.js
npm run watch        # rebuild on change
npm run test
npm run typecheck
```

## Releasing

Pushing a `streamdeck-obs-v*` tag (e.g. `streamdeck-obs-v1.0.0`) runs
[`.github/workflows/streamdeck-obs-release.yml`](../../../.github/workflows/streamdeck-obs-release.yml),
which builds, tests, packages the `.sdPlugin` folder into a `.streamDeckPlugin` zip, and attaches it
to a GitHub Release. Bump `Version` in
[`bot.nomnomzbot.streamdeck.obs.sdPlugin/manifest.json`](bot.nomnomzbot.streamdeck.obs.sdPlugin/manifest.json)
to match before tagging.
