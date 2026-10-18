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
import { consentsKey, dataRequestsKey } from '@/api/yourData';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('data rights live sync', () => {
  it('a DataRightsChanged push marks requests and consents stale and leaves other lists alone', async () => {
    const hub = await import('../dashboardHub');
    const queries = new QueryClient();

    await hub.startDashboardHub(queries);
    await hub.watchChannel('c1');

    const connection = FakeHubConnection.at(FakeHubConnection.instances.length - 1);

    queries.setQueryData(dataRequestsKey(), []);
    queries.setQueryData(consentsKey(), []);
    queries.setQueryData(commandsKey('c1'), []);

    connection.receive('DataRightsChanged', { change: 'completed' });

    expect(queries.getQueryState(dataRequestsKey())?.isInvalidated).toBe(true);
    expect(queries.getQueryState(consentsKey())?.isInvalidated).toBe(true);
    expect(queries.getQueryState(commandsKey('c1'))?.isInvalidated).toBe(false);
  });
});
