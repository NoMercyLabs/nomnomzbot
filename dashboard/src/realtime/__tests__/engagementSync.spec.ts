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
import { engagementConfigKey } from '@/api/engagement';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

async function started(): Promise<[QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub');
  const queries = new QueryClient();

  await hub.startDashboardHub(queries);
  await hub.watchChannel('c1');

  return [queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)];
}

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

describe('greetings and streaks live sync', () => {
  it('an engagement change marks the engagement config stale and leaves the command list alone', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(engagementConfigKey('c1'), { greetCooldownSeconds: 5 });
    queries.setQueryData(commandsKey('c1'), []);

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'engagement',
      entityId: null,
      action: 'updated',
    });

    expect(queries.getQueryState(engagementConfigKey('c1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(commandsKey('c1'))?.isInvalidated).toBe(false);
  });
});
