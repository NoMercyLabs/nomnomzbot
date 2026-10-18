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

import { commandsKey } from '@/api/commands';
import { federationOptInsKey } from '@/api/federation';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('federation live sync', () => {
  it('a FederationOptInChanged push marks the opt-in list stale and leaves other lists alone', async () => {
    const hub = await import('../dashboardHub');
    const queries = new QueryClient();

    await hub.startDashboardHub(queries);
    await hub.watchChannel('c1');

    const connection = FakeHubConnection.at(FakeHubConnection.instances.length - 1);

    queries.setQueryData(federationOptInsKey('c1'), []);
    queries.setQueryData(commandsKey('c1'), []);

    connection.receive('FederationOptInChanged', {
      peerId: null,
      optInType: 'shared_ban_list',
      direction: 'both',
      isEnabled: true,
    });

    expect(queries.getQueryState(federationOptInsKey('c1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(commandsKey('c1'))?.isInvalidated).toBe(false);
  });
});
