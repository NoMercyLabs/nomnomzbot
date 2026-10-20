// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

import { STREAM_DECK_MUSIC_ACTIONS } from '../streamDeckActions';

const plugin = join(__dirname, '../../../../tools/streamdeck/music');
const prefix = 'bot.nomnomzbot.streamdeck.music.';

type ManifestAction = { UUID: string };

function manifestOrder(): string[] {
  const path = join(plugin, 'bot.nomnomzbot.streamdeck.music.sdPlugin/manifest.json');
  const manifest = JSON.parse(readFileSync(path, 'utf8')) as { Actions: ManifestAction[] };

  return manifest.Actions.map((action) =>
    action.UUID.slice(prefix.length));
}

// What each plugin key really runs: its UUID and its backend action type, read from the plugin source.
function pluginTypes(): Record<string, string> {
  const folder = join(plugin, 'src/actions');
  const types: Record<string, string> = {};

  for (const name of readdirSync(folder)) {
    const text = readFileSync(join(folder, name), 'utf8');
    const uuid = /UUID: "bot\.nomnomzbot\.streamdeck\.music\.([a-z-]+)"/.exec(text)?.[1];
    const type = /actionType = "([a-z_]+)"/.exec(text)?.[1];

    if (uuid !== undefined && type !== undefined) {
      types[uuid] = type;
    }
  }

  return types;
}

// The setting each plugin key starts with: `{ volume: settings.volume ?? 50 }`. An empty text is no default.
function pluginDefaults(): Record<string, Record<string, unknown>> {
  const folder = join(plugin, 'src/actions');
  const defaults: Record<string, Record<string, unknown>> = {};

  for (const name of readdirSync(folder)) {
    const text = readFileSync(join(folder, name), 'utf8');
    const uuid = /UUID: "bot\.nomnomzbot\.streamdeck\.music\.([a-z-]+)"/.exec(text)?.[1];
    const found = /return \{ (\w+): settings\.\w+ \?\? ([^}]+?) \}/.exec(text);

    if (uuid !== undefined && found?.[1] !== undefined && found[2] !== undefined && found[2] !== '""') {
      defaults[uuid] = { [found[1]]: JSON.parse(found[2]) };
    }
  }

  return defaults;
}

describe('the Stream Deck music action catalog', () => {
  it('lists every plugin key, in the plugin order', () => {
    expect(STREAM_DECK_MUSIC_ACTIONS.map((action) =>
      action.key)).toEqual(manifestOrder());
  });

  it('starts each key with the plugin default setting', () => {
    const defaults = pluginDefaults();

    for (const action of STREAM_DECK_MUSIC_ACTIONS) {
      const own = 'defaults' in action
        ? action.defaults
        : {};

      expect([action.key, own]).toEqual([action.key, defaults[action.key] ?? {}]);
    }
  });

  it('runs the same bot action as the plugin key it copies', () => {
    const types = pluginTypes();

    for (const action of STREAM_DECK_MUSIC_ACTIONS) {
      expect([action.key, action.type]).toEqual([action.key, types[action.key]]);
    }
  });
});
