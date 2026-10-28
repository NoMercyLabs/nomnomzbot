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

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

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
