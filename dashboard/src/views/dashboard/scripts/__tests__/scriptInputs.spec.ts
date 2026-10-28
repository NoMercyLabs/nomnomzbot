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

import type { TestTrigger } from '@/composables/useEditorBridge';

import { eventsFor, readInputs } from '../components/scriptInputs';

const follow = {
  id: 'f',
  label: 'Follow',
  variables: { user: 'a', followed_at: 'now' },
};
const timer = {
  id: 't',
  label: 'Timer',
  variables: { 'timer.name': 'x' },
};
const cheer = {
  id: 'c',
  label: 'Cheer',
  variables: { user: 'b', bits: '5' },
};
const all: TestTrigger[] = [follow, timer, cheer];

describe('readInputs', () => {
  it('lists each variable the script reads once, in the order first read', () => {
    const source = `const a = bot.getVar('user'); const b = bot.getVar("bits"); bot.getVar('user');`;

    expect(readInputs(source)).toEqual({ variables: ['user', 'bits'], usesArgs: false });
  });

  it('says when the script reads the words after the command', () => {
    expect(readInputs('const w = bot.args[0] ?? "x";')).toEqual({ variables: [], usesArgs: true });
  });

  it('reads nothing from a script that asks the trigger for nothing', () => {
    expect(readInputs('nnz.random.int(1, 6);')).toEqual({ variables: [], usesArgs: false });
  });
});

describe('eventsFor', () => {
  it('keeps only the events that hand over a variable the script reads', () => {
    expect(eventsFor(all, ['user'])).toEqual([follow, cheer]);
    expect(eventsFor(all, ['timer.name'])).toEqual([timer]);
  });

  it('keeps no event for a script that reads no variable', () => {
    expect(eventsFor(all, [])).toEqual([]);
  });
});
