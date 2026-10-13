// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient } from '@tanstack/vue-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { communityListKey, viewerProfileKey } from '@/api/people';

import { FakeHubConnection, FakeHubConnectionBuilder } from './fakeSignalR';

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: FakeHubConnectionBuilder,
  HubConnection: FakeHubConnection,
}));

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
