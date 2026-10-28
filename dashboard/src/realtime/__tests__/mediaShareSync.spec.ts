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
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { mediaShareConfigKey, mediaShareQueueKey } from '@/api/mediaShare';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('media share live sync', () => {
  it('a playback change marks the queue of that channel stale and leaves the config alone', async () => {
    const hub = await import('../dashboardHub');
    const queries = new QueryClient();

    await hub.startDashboardHub(queries);
    await hub.watchChannel('c1');

    const connection = FakeHubConnection.at(FakeHubConnection.instances.length - 1);

    queries.setQueryData(mediaShareQueueKey('c1'), []);
    queries.setQueryData(mediaShareQueueKey('other'), []);
    queries.setQueryData(mediaShareConfigKey('c1'), {});

    connection.receive('ChannelEvent', {
      type: 'media_share_playback_changed',
      broadcasterId: 'c1',
      userId: null,
      userDisplayName: null,
      data: { requestId: 'r1', status: 'playing' },
      timestamp: '2026-10-01T10:00:00Z',
    });

    expect(queries.getQueryState(mediaShareQueueKey('c1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(mediaShareQueueKey('other'))?.isInvalidated).toBe(false);
    expect(queries.getQueryState(mediaShareConfigKey('c1'))?.isInvalidated).toBe(false);
  });
});
