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
import { describe, expect, it } from 'vitest';

import { adminTenantKey, adminTenantsKey, type AdminTenantsParams } from '@/api/admin';

import { adminHandlers } from '../admin';
import type { AdminChannelRegistryPayload } from '../payloads';

type Page = { data: Record<string, unknown>[]; total: number };

// A tenant row is a person; a streamer carries the state of their channel, a viewer carries none.
const row = (id: string, status: string, isLive: boolean) => ({
  userId: `user-${id}`,
  displayName: id,
  kind: 'streamer',
  channel: {
    broadcasterId: id,
    name: id,
    status,
    isLive,
  },
}) as const satisfies Record<string, unknown>;

const viewer = (id: string) => ({
  userId: `user-${id}`,
  displayName: id,
  kind: 'viewer',
  channel: null,
}) as const satisfies Record<string, unknown>;

const channelOf = (entry: Record<string, unknown> | undefined) =>
  entry?.channel as Record<string, unknown> | null | undefined;
const first: AdminTenantsParams = { page: 1, take: 25 };
const second: AdminTenantsParams = { page: 2, take: 25 };

function push(queries: QueryClient, payload: AdminChannelRegistryPayload): void {
  const handler = adminHandlers.find((h) => h.event === 'ReceiveChannelRegistryUpdate');

  expect(handler).toBeDefined();

  (handler as { handle: (c: unknown, p: AdminChannelRegistryPayload) => void }).handle(
    { queries, channelId: null },
    payload,
  );
}

function seed(): QueryClient {
  const queries = new QueryClient();

  queries.setQueryData<Page>(adminTenantsKey(first), {
    data: [row('a', 'active', false), row('b', 'active', false), viewer('v')],
    total: 4,
  });

  queries.setQueryData<Page>(adminTenantsKey(second), {
    data: [row('c', 'active', false)],
    total: 4,
  });

  return queries;
}

describe('ReceiveChannelRegistryUpdate', () => {
  it('flips isLive on that row in a cached page and leaves the other rows and pages alone', () => {
    const queries = seed();

    push(queries, {
      broadcasterId: 'c',
      channelName: 'c',
      isLive: true,
      streamTitle: 't',
      gameName: 'g',
    });

    const page2 = queries.getQueryData<Page>(adminTenantsKey(second))!;

    expect(channelOf(page2.data[0])).toMatchObject({
      broadcasterId: 'c',
      isLive: true,
      status: 'active',
    });

    expect(page2.data[0]).toMatchObject({ userId: 'user-c', kind: 'streamer' });
    expect(page2.total).toBe(4);
    const page1 = queries.getQueryData<Page>(adminTenantsKey(first))!;

    expect(page1.data.map((r) => channelOf(r)?.isLive)).toEqual([false, false, undefined]);
    expect(queries.getQueryState(adminTenantsKey(second))?.isInvalidated).toBe(false);
  });

  it('takes a channel offline', () => {
    const queries = seed();

    push(queries, {
      broadcasterId: 'a',
      channelName: 'a',
      isLive: true,
    });

    const live = queries.getQueryData<Page>(adminTenantsKey(first))!.data[0];

    expect(channelOf(live)?.isLive).toBe(true);

    push(queries, {
      broadcasterId: 'a',
      channelName: 'a',
      isLive: false,
    });

    const person = queries.getQueryData<Page>(adminTenantsKey(first))!.data[0];

    expect(channelOf(person)).toMatchObject({
      broadcasterId: 'a',
      isLive: false,
    });
  });

  it('changes the status shown in the list and in the cached detail, and leaves isLive alone', () => {
    const queries = seed();

    queries.setQueryData(adminTenantKey('a'), {
      id: 'a',
      status: 'active',
      suspendedReason: null,
    });

    push(queries, { broadcasterId: 'a', status: 'Suspended' });

    const rows = queries.getQueryData<Page>(adminTenantsKey(first))!.data;

    expect(channelOf(rows[0])).toMatchObject({
      broadcasterId: 'a',
      status: 'Suspended',
      isLive: false,
    });

    expect(queries.getQueryData(adminTenantKey('a'))).toMatchObject({
      id: 'a',
      status: 'Suspended',
    });

    expect(channelOf(rows[1])).toMatchObject({
      broadcasterId: 'b',
      status: 'active',
    });
  });

  it('leaves a viewer row, which has no channel, exactly as it was', () => {
    const queries = seed();

    push(queries, {
      broadcasterId: 'a',
      isLive: true,
      status: 'Suspended',
    });

    const rows = queries.getQueryData<Page>(adminTenantsKey(first))!.data;

    expect(rows[2]).toEqual(viewer('v'));
  });

  it('does not throw for an unknown id and marks the lists stale', () => {
    const queries = seed();

    expect(() =>
      push(queries, {
        broadcasterId: 'zzz',
        channelName: 'z',
        isLive: true,
      }),
    ).not.toThrow();

    expect(queries.getQueryState(adminTenantsKey(first))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(adminTenantsKey(second))?.isInvalidated).toBe(true);

    const rows = queries.getQueryData<Page>(adminTenantsKey(first))!.data;

    expect(rows.map((r) => channelOf(r)?.isLive)).toEqual([false, false, undefined]);
  });

  it('does not throw when nothing is cached', () => {
    expect(() => push(new QueryClient(), { broadcasterId: 'a', isLive: true })).not.toThrow();
  });
});

type Invalidating = {
  event: string;
  payload: unknown;
  stale: readonly unknown[];
  fresh: readonly unknown[];
};

function fire(queries: QueryClient, event: string, payload: unknown): void {
  const handler = adminHandlers.find((h) => h.event === event);

  expect(handler).toBeDefined();

  (handler as { handle: (c: unknown, p: unknown) => void }).handle(
    { queries, channelId: null },
    payload,
  );
}

describe('admin pages that reload on a hub event', () => {
  const cases: Invalidating[] = [
    {
      event: 'ReceivePlatformDefaultsChanged',
      payload: { set: 'tts-voice', key: null },
      stale: ['admin', 'platform-defaults', 'tts-voice'],
      fresh: ['admin', 'scheduled-jobs'],
    },
    {
      event: 'ReceiveScheduledJobsChanged',
      payload: {
        change: 'retried',
        originalTaskId: 'a',
        newTaskId: 'b',
      },
      stale: ['admin', 'scheduled-jobs', { page: 1 }],
      fresh: ['admin', 'platform-defaults', 'tts-voice'],
    },
    {
      event: 'ReceiveBillingChanged',
      payload: {
        area: 'priced-unit',
        action: 'updated',
        subjectId: 'u',
      },
      stale: ['admin', 'tenant-usage', { page: 1 }],
      fresh: ['admin', 'platform-defaults', 'tts-voice'],
    },
  ];

  it.each(cases)('$event reloads its own queries and no others', ({ event, payload, stale, fresh }) => {
    const queries = new QueryClient();

    queries.setQueryData(stale, 1);
    queries.setQueryData(fresh, 1);

    fire(queries, event, payload);

    expect(queries.getQueryState(stale)?.isInvalidated).toBe(true);
    expect(queries.getQueryState(fresh)?.isInvalidated).toBe(false);
  });

  it('a billing change in another area leaves the tenant usage alone', () => {
    const queries = new QueryClient();

    queries.setQueryData(['admin', 'tenant-usage', { page: 1 }], 1);

    fire(queries, 'ReceiveBillingChanged', {
      area: 'tier',
      action: 'updated',
      subjectId: 't',
    });

    expect(queries.getQueryState(['admin', 'tenant-usage', { page: 1 }])?.isInvalidated).toBe(false);
  });
});
