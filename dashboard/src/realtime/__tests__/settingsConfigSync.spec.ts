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

import { commandsKey } from '@/api/commands';
import {
  discordConnectionsKey,
  discordRulesKey,
  liveRoleKey,
  notifyRolesKey,
} from '@/api/discord';
import { escalationPolicyKey } from '@/api/escalationPolicy';
import { networkBlockOptInKey } from '@/api/networkBlockOptIn';
import { spamDefensePolicyKey } from '@/api/spamDefensePolicy';

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

function change(domain: string, action: string) {
  return {
    broadcasterId: 'x',
    domain,
    entityId: null,
    action,
  };
}

function stale(queries: QueryClient, key: readonly unknown[]): boolean | undefined {
  return queries.getQueryState(key)?.isInvalidated;
}

describe('settings pages live sync', () => {
  it('a network-blocks change marks the opt-in card stale and leaves the command list alone', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(networkBlockOptInKey('c1'), { optedIn: true });
    queries.setQueryData(commandsKey('c1'), []);

    connection.receive('ConfigChanged', change('network-blocks', 'updated'));

    expect(stale(queries, networkBlockOptInKey('c1'))).toBe(true);
    expect(stale(queries, commandsKey('c1'))).toBe(false);
  });

  it('an escalation change marks the escalation policy stale and leaves the command list alone', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(escalationPolicyKey('c1'), { steps: [] });
    queries.setQueryData(commandsKey('c1'), []);

    connection.receive('ConfigChanged', change('escalation', 'updated'));

    expect(stale(queries, escalationPolicyKey('c1'))).toBe(true);
    expect(stale(queries, commandsKey('c1'))).toBe(false);
  });

  it('a spam-defense change marks the spam policy stale and leaves the command list alone', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(spamDefensePolicyKey('c1'), { settings: {} });
    queries.setQueryData(commandsKey('c1'), []);

    connection.receive('ConfigChanged', change('spam-defense', 'updated'));

    expect(stale(queries, spamDefensePolicyKey('c1'))).toBe(true);
    expect(stale(queries, commandsKey('c1'))).toBe(false);
  });

  it.each(['created', 'updated', 'deleted'])(
    'a discord %s change marks every discord query of every connection stale and leaves the command list alone',
    async (action) => {
      const [queries, connection] = await started();
      const discordKeys = [
        discordConnectionsKey('c1'),
        discordRulesKey('c1', 'g1'),
        notifyRolesKey('c1', 'g1'),
        liveRoleKey('c1', 'g1'),
      ];

      discordKeys.forEach((key) => queries.setQueryData(key, []));
      queries.setQueryData(commandsKey('c1'), []);

      connection.receive('ConfigChanged', change('discord', action));

      discordKeys.forEach((key) => expect(stale(queries, key)).toBe(true));
      expect(stale(queries, commandsKey('c1'))).toBe(false);
    },
  );
});
