// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

import { oneOf } from '../lib/array';

// Stoney 2026-10-18: Longest song, in seconds, needs to be minutes: don't make the user do math. A length of
// time goes through <DurationInput> (minutes and seconds on screen, seconds in the model). These files still
// hold a raw number field for seconds; the sweep removes them from the list, and a clean file must leave it.
const src = join(__dirname, '..');
const rawSecondsField = /<Input\b[^>]*?type="number"[^>]*?seconds[^>]*?\/>|<Input\b[^>]*?seconds[^>]*?type="number"[^>]*?\/>/is;
// StudioPanel stays: an OBS transition lasts tenths of a second (0.3 s), and DurationInput counts whole seconds.
const sweepList = [
  'views/dashboard/commands/components/CommandEditor.vue',
  'views/dashboard/obs/components/StudioPanel.vue',
  'views/dashboard/timers/components/ChatTriggerEditor.vue',
  'views/dashboard/voice-triggers/components/NewVoiceTriggerDialog.vue',
  'views/dashboard/voice-triggers/components/VoiceTriggerEditor.vue',
] as const satisfies readonly string[];

function vueSources(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory())
      return entry.name === '__tests__'
        ? []
        : vueSources(path);

    return path.endsWith('.vue')
      ? [relative(src, path).replace(/\\/g, '/')]
      : [];
  });
}

function holdsRawSecondsField(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return rawSecondsField.test(text);
}

describe('duration fields', () => {
  it('spots a raw number field for seconds', () => {
    const raw = '<Input v-model.number="cooldownSeconds" type="number" />';

    expect(rawSecondsField.test(raw)).toBe(true);
  });

  it('has no raw number field for seconds outside the sweep list', () => {
    const offenders = vueSources(src)
      .filter((path) => holdsRawSecondsField(path))
      .filter((path) => oneOf(sweepList, path) === undefined);

    expect(offenders).toEqual([]);
  });

  it('keeps no clean file on the sweep list', () => {
    const stale = sweepList.filter((path) => !holdsRawSecondsField(path));

    expect(stale).toEqual([]);
  });
});
