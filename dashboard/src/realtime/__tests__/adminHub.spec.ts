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

import { FakeHubConnection, FakeHubConnectionBuilder } from './fakeSignalR';

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: FakeHubConnectionBuilder,
  HubConnection: FakeHubConnection,
}));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('admin hub events', () => {
  it('a system status push replaces the cached snapshot', async () => {
    const { startAdminHub } = await import('../adminHub');
    const { adminSystemStatusKey } = await import('@/api/admin');
    const queries = new QueryClient();

    await startAdminHub(queries);

    const snapshot = { system: { uptime: 5 }, stats: { channels: 2 } };

    FakeHubConnection.at(0).receive('ReceiveSystemStatus', snapshot);

    expect(queries.getQueryData(adminSystemStatusKey)).toEqual(snapshot);
  });
});
