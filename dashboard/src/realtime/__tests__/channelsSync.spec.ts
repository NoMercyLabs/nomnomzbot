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

import { channelInfoKey, deletedChannelsKey } from '@/api/channelManagement';
import { myChannelsQuery } from '@/api/channels';
import { commandsKey } from '@/api/commands';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('channels live sync', () => {
  it.each(['joined', 'left', 'deleted', 'restored'])('a channels ConfigChanged (%s) marks the channel lists and the channel info stale and leaves other lists alone', async (action) => {
    const hub = await import('../dashboardHub');
    const queries = new QueryClient();

    await hub.startDashboardHub(queries);
    await hub.watchChannel('c1');

    const connection = FakeHubConnection.at(FakeHubConnection.instances.length - 1);

    queries.setQueryData(myChannelsQuery.queryKey, []);
    queries.setQueryData(deletedChannelsKey(), []);
    queries.setQueryData(channelInfoKey('c1'), {});
    queries.setQueryData(commandsKey('c1'), []);

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'channels',
      entityId: 'c1',
      action,
    });

    expect(queries.getQueryState(myChannelsQuery.queryKey)?.isInvalidated).toBe(true);
    expect(queries.getQueryState(deletedChannelsKey())?.isInvalidated).toBe(true);
    expect(queries.getQueryState(channelInfoKey('c1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(commandsKey('c1'))?.isInvalidated).toBe(false);
  });
});
