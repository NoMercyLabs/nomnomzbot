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
