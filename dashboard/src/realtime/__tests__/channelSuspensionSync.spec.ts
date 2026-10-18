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

import { myChannelsQuery } from '@/api/channels';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

async function started(): Promise<[QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub');
  const queries = new QueryClient();

  await hub.startDashboardHub(queries);
  await hub.watchChannel('c1');

  return [queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)];
}

describe('a suspension or a reinstatement from the hub', () => {
  it.each(['suspended', 'active'])('refetches the channel list, which carries the suspension (%s)', async (status) => {
    const [queries, connection] = await started();

    queries.setQueryData(myChannelsQuery.queryKey, []);

    connection.receive('ChannelSuspensionChanged', {
      broadcasterId: 'c1',
      status,
      reason: null,
      timestamp: '2026-10-18T10:00:00Z',
    });

    expect(queries.getQueryState(myChannelsQuery.queryKey)?.isInvalidated).toBe(true);
  });
});
