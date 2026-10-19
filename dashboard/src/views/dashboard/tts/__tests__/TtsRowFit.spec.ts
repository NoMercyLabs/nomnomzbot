// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

import en from '../i18n/en.json';

// A row in the 560 px Speaking and Rules panels has about 290 px of text beside its control.
// A help line over this length wraps to a second line (owner: no line wrapping, make it wider).
const LONGEST_HELP = 52;

type Described = { description?: string };

function helpLines(section: Record<string, unknown>): [string, string][] {
  return Object.entries(section).flatMap(([key, value]) => {
    const text = (value as Described).description;

    return typeof text === 'string'
      ? [[key, text]] as const satisfies readonly [string, string][]
      : [] as const satisfies readonly [string, string][];
  });
}

// The panel header text starts 16 px in (px-4); the body adds 8 px (p-2). The add form must add 8 more, not 12.
describe('the pronunciation add form', () => {
  it('lines up with the panel header text', () => {
    const source = readFileSync(resolve(__dirname, '../LexiconPanel.vue'), 'utf8');
    const form = /<form[^>]*class="([^"]*)"/.exec(source)?.[1] ?? '';
    const classes = form.split(/\s+/);

    expect(classes).toContain('px-2');
    expect(classes).not.toContain('p-3');
  });
});

describe('the help lines of the Speaking and Rules rows', () => {
  const sections = {
    rules: en.dashboard.tts.rules,
    speaking: en.dashboard.tts.speaking,
  } as const;

  for (const [name, section] of Object.entries(sections)) {
    it(`fit on one line in ${name}`, () => {
      const tooLong = helpLines(section).filter(([, text]) => text.length > LONGEST_HELP);

      expect(tooLong).toEqual([]);
    });
  }
});
