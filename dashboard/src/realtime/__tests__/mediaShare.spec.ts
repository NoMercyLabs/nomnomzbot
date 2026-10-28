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

import { QueryClient } from '@tanstack/vue-query';
import { describe, expect, it } from 'vitest';

import { mediaSharePlayerKey, mediaShareQueueKey } from '@/api/mediaShare';

import { mediaShareHandlers } from '../mediaShare';
import type { ChannelEventPayload } from '../payloads';

const state = {
  state: 'paused',
  requestId: 'r1',
  title: 'Bonk',
  sharerName: 'viewer',
  thumbnailUrl: null,
  positionSeconds: 4,
  durationSeconds: 43,
  volume: 35,
};

const event = (type: string): ChannelEventPayload => ({
  type,
  broadcasterId: 'c1',
  userId: null,
  userDisplayName: null,
  data: state,
  timestamp: '2026-10-20T10:00:00Z',
});

describe('media share realtime', () => {
  it('stores the reported player state and marks the queue stale', () => {
    const queries = new QueryClient();

    queries.setQueryData(mediaShareQueueKey('c1'), []);

    for (const handler of mediaShareHandlers) {
      handler.handle({ queries, channelId: 'c1' }, event('media_share_player_state'));
    }

    expect(queries.getQueryData(mediaSharePlayerKey('c1'))).toEqual(state);
    expect(queries.getQueryState(mediaShareQueueKey('c1'))?.isInvalidated).toBe(true);
  });

  it('leaves the player alone on any other channel event', () => {
    const queries = new QueryClient();

    for (const handler of mediaShareHandlers) {
      handler.handle({ queries, channelId: 'c1' }, event('shield_mode_begin'));
    }

    expect(queries.getQueryData(mediaSharePlayerKey('c1'))).toBeUndefined();
  });
});
