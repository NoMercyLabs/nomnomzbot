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

import { actionRequiredKey } from '@/api/actionRequired';
import { requiredOverlaysKey, widgetsKey } from '@/api/widgets';

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

describe('overlay widgets live sync', () => {
  it.each(['created', 'updated', 'deleted'])(
    'a widgets %s change marks the joined channel widget list stale',
    async (action: string) => {
      const [queries, connection] = await started();

      queries.setQueryData(widgetsKey('c1'), []);
      queries.setQueryData(widgetsKey('other'), []);

      connection.receive('ConfigChanged', {
        broadcasterId: 'x',
        domain: 'widgets',
        entityId: 'w1',
        action,
      });

      expect(queries.getQueryState(widgetsKey('c1'))?.isInvalidated).toBe(true);
      expect(queries.getQueryState(widgetsKey('other'))?.isInvalidated).toBe(false);
    },
  );

  it('an overlay connect signal marks the widget list and the required overlays stale', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(widgetsKey('c1'), []);
    queries.setQueryData(requiredOverlaysKey('c1'), []);

    connection.receive('ConfigChanged', {
      broadcasterId: 'c1',
      domain: 'widgets',
      entityId: 'w1',
      action: 'updated',
    });

    expect(queries.getQueryState(widgetsKey('c1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(requiredOverlaysKey('c1'))?.isInvalidated).toBe(true);
  });

  it('a notifications change still marks only the action-required inbox stale', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(widgetsKey('c1'), []);
    queries.setQueryData(actionRequiredKey('c1'), []);

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'notifications',
      entityId: null,
      action: 'updated',
    });

    expect(queries.getQueryState(actionRequiredKey('c1'))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(widgetsKey('c1'))?.isInvalidated).toBe(false);
  });
});
