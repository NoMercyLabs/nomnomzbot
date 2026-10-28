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

import { communityListKey, viewerProfileKey } from '@/api/people';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('ModAction', () => {
  it('marks every people list and every viewer profile of the joined channel stale, and no other channel', async () => {
    const hub = await import('../dashboardHub');
    const queries = new QueryClient();

    await hub.startDashboardHub(queries);
    const connection = FakeHubConnection.at(FakeHubConnection.instances.length - 1);

    await hub.watchChannel('c1');
    queries.setQueryData(communityListKey('c1', 'moderator', ''), { pages: [], pageParams: [] });
    queries.setQueryData(viewerProfileKey('c1', 'g1'), {});
    queries.setQueryData(communityListKey('c2', 'all', ''), { pages: [], pageParams: [] });

    connection.receive('ModAction', {
      action: 'timeout',
      moderatorId: 'm',
      targetUserId: 't1',
      reason: null,
      durationSeconds: 60,
      broadcasterId: 'b',
    });

    expect(queries.getQueryState(communityListKey('c1', 'moderator', ''))?.isInvalidated).toBe(
      true,
    );

    expect(queries.getQueryState(viewerProfileKey('c1', 'g1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(communityListKey('c2', 'all', ''))?.isInvalidated).toBe(false);
  });
});
