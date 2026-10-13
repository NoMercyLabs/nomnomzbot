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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { FakeHubConnection, FakeHubConnectionBuilder } from './fakeSignalR';

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: FakeHubConnectionBuilder,
  HubConnection: FakeHubConnection,
}));

beforeEach(() => {
  vi.resetModules();
  vi.useFakeTimers();
  FakeHubConnection.instances = [];
  FakeHubConnection.startFailures = 0;
  FakeHubConnection.startCalls = 0;
  FakeHubConnectionBuilder.policy = null;
});

afterEach(() => {
  vi.useRealTimers();
});

describe('hub start', () => {
  it('a start that fails twice is tried again and then joins the channel', async () => {
    const hub = await import('../dashboardHub');
    const { dashboardHubStatus } = await import('../hubStatus');

    FakeHubConnection.startFailures = 2;
    await hub.watchChannel('c1');

    const starting = hub.startDashboardHub(new QueryClient());

    await vi.advanceTimersByTimeAsync(60_000);
    await starting;

    const connection = FakeHubConnection.at(0);

    expect(FakeHubConnection.startCalls).toBe(3);
    expect(dashboardHubStatus.value).toBe('connected');
    expect(connection.invocations).toContainEqual(['JoinChannel', 'c1']);
  });

  it('a channel change during the start sends nothing, does not throw, and joins once the start resolves', async () => {
    const hub = await import('../dashboardHub');

    FakeHubConnection.startFailures = 1;

    const starting = hub.startDashboardHub(new QueryClient());

    await vi.advanceTimersByTimeAsync(0);
    await expect(hub.watchChannel('c2')).resolves.toBeUndefined();
    expect(FakeHubConnection.at(0).invocations).toEqual([]);

    await vi.advanceTimersByTimeAsync(60_000);
    await starting;

    expect(FakeHubConnection.at(0).invocations).toEqual([['JoinChannel', 'c2']]);
  });

  it('stopping the hub while it retries ends the retry loop', async () => {
    const hub = await import('../dashboardHub');
    const { dashboardHubStatus } = await import('../hubStatus');

    FakeHubConnection.startFailures = 1000;

    const starting = hub.startDashboardHub(new QueryClient());

    await vi.advanceTimersByTimeAsync(5_000);
    await hub.stopDashboardHub();
    await vi.advanceTimersByTimeAsync(120_000);
    await starting;

    const calls = FakeHubConnection.startCalls;

    await vi.advanceTimersByTimeAsync(120_000);

    expect(FakeHubConnection.startCalls).toBe(calls);
    expect(dashboardHubStatus.value).toBe('offline');
  });

  it('a refused join lands in the join errors', async () => {
    const hub = await import('../dashboardHub');
    const { dashboardJoinErrors } = await import('../hubStatus');

    await hub.startDashboardHub(new QueryClient());
    FakeHubConnection.at(0).answer = () => ({ success: false, error: 'Access denied' });
    await hub.watchChannel('c1');

    expect(dashboardJoinErrors.value).toEqual([{ channelId: 'c1', message: 'Access denied' }]);
  });
});

describe('hub reconnect', () => {
  it('the retry policy never gives up and its delay stays capped', async () => {
    const hub = await import('../dashboardHub');

    await hub.startDashboardHub(new QueryClient());

    const policy = FakeHubConnectionBuilder.policy;

    if (typeof policy !== 'object' || policy === null)
      throw new Error('no retry policy was set');

    const delays = Array.from({ length: 1001 }, (_, previousRetryCount) =>
      policy.nextRetryDelayInMilliseconds({
        previousRetryCount,
        elapsedMilliseconds: previousRetryCount * 30_000,
        retryReason: new Error('down'),
      }));

    expect(delays.every((delay) => delay !== null && delay >= 0 && delay <= 30_000)).toBe(true);
    expect(delays[1000]).toBe(30_000);
  });

  it('after a reconnect the active queries are marked stale and the channel is joined again', async () => {
    const hub = await import('../dashboardHub');
    const queries = new QueryClient();

    await hub.startDashboardHub(queries);
    await hub.watchChannel('c1');
    queries.setQueryData(['channels', 'c1', 'timers'], []);
    queries.setQueryData(['dashboard', 'c1', 'stats'], {});

    const connection = FakeHubConnection.at(0);

    connection.invocations.length = 0;
    connection.lifecycle.reconnected();

    expect(queries.getQueryState(['channels', 'c1', 'timers'])?.isInvalidated).toBe(true);
    expect(queries.getQueryState(['dashboard', 'c1', 'stats'])?.isInvalidated).toBe(true);
    expect(connection.invocations).toContainEqual(['JoinChannel', 'c1']);
  });

  it('a channel change while reconnecting sends nothing and the reconnect joins the new channel', async () => {
    const hub = await import('../dashboardHub');

    await hub.startDashboardHub(new QueryClient());
    await hub.watchChannel('c1');

    const connection = FakeHubConnection.at(0);

    connection.lifecycle.reconnecting();
    connection.invocations.length = 0;
    await expect(hub.watchChannel('c2')).resolves.toBeUndefined();
    expect(connection.invocations).toEqual([]);

    connection.lifecycle.reconnected();

    expect(connection.invocations).toEqual([['JoinChannel', 'c2']]);
  });

  it('a join that throws because the link dropped is not an unhandled rejection', async () => {
    const hub = await import('../dashboardHub');

    await hub.startDashboardHub(new QueryClient());

    const connection = FakeHubConnection.at(0);

    connection.invoke = () => Promise.reject(new Error('Cannot send data'));

    await expect(hub.watchChannel('c1')).resolves.toBeUndefined();
  });
});
