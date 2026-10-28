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

import type { QueueItem } from '@/api/music';

import { requestersIn } from '../components/requesters';

const song = (position: number, requestedBy: string | null): QueueItem => ({
  position,
  number: position + 1,
  trackName: `Song ${position}`,
  artist: 'Artist',
  imageUrl: null,
  durationMs: 1000,
  requestedBy,
  cost: 0,
  waitsForMusicPlayer: false,
});

describe('requestersIn', () => {
  it('lists only people with queued songs, with their count, most songs first', () => {
    const rows = [song(0, 'Bamo16'), song(1, 'Other'), song(2, 'bamo16'), song(3, 'Bamo16')];

    expect(requestersIn(rows)).toEqual([
      { name: 'Bamo16', count: 3 },
      { name: 'Other', count: 1 },
    ]);
  });

  it('skips songs without a requester and lists nobody for an empty queue', () => {
    expect(requestersIn([song(0, null)])).toEqual([]);
    expect(requestersIn([])).toEqual([]);
  });
});
