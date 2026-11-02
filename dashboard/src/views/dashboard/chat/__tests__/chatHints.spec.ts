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

import { chatDisplayDefaults, lineHints, type ChatDisplay } from '../chatDisplay';

const now = new Date('2026-11-02T12:00:00Z');

const facts = (over: Partial<NonNullable<Parameters<typeof lineHints>[0]['hints']>> = {}) => ({
  firstEver: false,
  firstOfDay: false,
  firstOfStream: false,
  accountCreatedAt: null,
  ...over,
});

const all = {
  ...chatDisplayDefaults,
  hintFirstOfDay: true,
  hintFirstOfStream: true,
} as const satisfies ChatDisplay;

describe('lineHints', () => {
  it('shows only the most specific "first" hint', () => {
    expect(lineHints(
      { messageType: 'text', hints: facts({
        firstEver: true,
        firstOfDay: true,
        firstOfStream: true,
      }) },
      all,
      now,
    )).toEqual([{ key: 'firstEver' }]);

    expect(lineHints(
      { messageType: 'text', hints: facts({ firstOfDay: true, firstOfStream: true }) },
      all,
      now,
    )).toEqual([{ key: 'firstOfStream' }]);

    expect(lineHints(
      { messageType: 'text', hints: facts({ firstOfDay: true }) },
      all,
      now,
    )).toEqual([{ key: 'firstOfDay' }]);
  });

  it('keeps the first of the day and of the stream off until they are turned on', () => {
    expect(lineHints(
      { messageType: 'text', hints: facts({ firstOfDay: true, firstOfStream: true }) },
      chatDisplayDefaults,
      now,
    )).toEqual([]);
  });

  it('a turned-off first-ever switch hides it without promoting the smaller hints', () => {
    expect(lineHints(
      { messageType: 'text', hints: facts({ firstEver: true, firstOfDay: true }) },
      { ...all, hintFirstEver: false },
      now,
    )).toEqual([]);
  });

  it('marks an account younger than the chosen window, with its age in days', () => {
    const created = '2026-10-30T12:00:00Z';

    expect(lineHints(
      { messageType: 'text', hints: facts({ accountCreatedAt: created }) },
      chatDisplayDefaults,
      now,
    )).toEqual([{ key: 'newAccount', days: 3 }]);

    expect(lineHints(
      { messageType: 'text', hints: facts({ accountCreatedAt: created }) },
      { ...chatDisplayDefaults, newAccountDays: '1' },
      now,
    )).toEqual([]);
  });

  it('leaves an old account, an unknown account date and a switched-off setting unmarked', () => {
    expect(lineHints(
      { messageType: 'text', hints: facts({ accountCreatedAt: '2020-01-01T00:00:00Z' }) },
      chatDisplayDefaults,
      now,
    )).toEqual([]);

    expect(lineHints({ messageType: 'text', hints: facts() }, chatDisplayDefaults, now)).toEqual([]);

    expect(lineHints(
      { messageType: 'text', hints: facts({ accountCreatedAt: '2026-11-01T00:00:00Z' }) },
      { ...chatDisplayDefaults, hintNewAccount: false },
      now,
    )).toEqual([]);
  });

  it('hints highlighted, sub-only and intro messages from their message type, even with no facts', () => {
    expect(lineHints({ messageType: 'channel_points_highlighted' }, chatDisplayDefaults, now))
      .toEqual([{ key: 'highlighted' }]);

    expect(lineHints({ messageType: 'channel_points_sub_only' }, chatDisplayDefaults, now))
      .toEqual([{ key: 'subOnly' }]);

    expect(lineHints({ messageType: 'user_intro' }, chatDisplayDefaults, now))
      .toEqual([{ key: 'intro' }]);

    expect(lineHints(
      { messageType: 'user_intro' },
      { ...chatDisplayDefaults, hintMessageType: false },
      now,
    )).toEqual([]);
  });
});
