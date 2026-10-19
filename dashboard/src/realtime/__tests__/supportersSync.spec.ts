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

import { timersKey } from '@/api/liveOps';
import { supporterEventsKey } from '@/api/supporters';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('supporters live sync', () => {
  it('a supporters config change marks the tips list stale and leaves timers alone', async () => {
    const hub = await import('../dashboardHub');
    const queries = new QueryClient();

    await hub.startDashboardHub(queries);
    await hub.watchChannel('c1');

    const connection = FakeHubConnection.at(FakeHubConnection.instances.length - 1);

    queries.setQueryData(supporterEventsKey('c1'), []);
    queries.setQueryData(timersKey('c1'), []);

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'supporters',
      entityId: 'e1',
      action: 'created',
    });

    expect(queries.getQueryState(supporterEventsKey('c1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(timersKey('c1'))?.isInvalidated).toBe(false);
  });
});
