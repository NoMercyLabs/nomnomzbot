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
import { describe, expect, it } from 'vitest';

import { adminTenantKey, adminTenantsKey, type AdminTenantsParams } from '@/api/admin';

import { adminHandlers } from '../admin';
import type { AdminChannelRegistryPayload } from '../payloads';

type Page = { data: Record<string, unknown>[]; total: number };

const row = (id: string, status: string, isLive: boolean) => ({
  id,
  name: id,
  status,
  isLive,
}) as const satisfies Record<string, unknown>;
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
    data: [row('a', 'active', false), row('b', 'active', false)],
    total: 3,
  });

  queries.setQueryData<Page>(adminTenantsKey(second), {
    data: [row('c', 'active', false)],
    total: 3,
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

    expect(page2.data[0]).toMatchObject({
      id: 'c',
      isLive: true,
      status: 'active',
    });

    expect(page2.total).toBe(3);
    const page1 = queries.getQueryData<Page>(adminTenantsKey(first))!;

    expect(page1.data.map((r) => r.isLive)).toEqual([false, false]);
    expect(queries.getQueryState(adminTenantsKey(second))?.isInvalidated).toBe(false);
  });

  it('takes a channel offline', () => {
    const queries = seed();

    push(queries, {
      broadcasterId: 'a',
      channelName: 'a',
      isLive: true,
    });

    push(queries, {
      broadcasterId: 'a',
      channelName: 'a',
      isLive: false,
    });

    expect(queries.getQueryData<Page>(adminTenantsKey(first))!.data[0]).toMatchObject({
      id: 'a',
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

    expect(queries.getQueryData<Page>(adminTenantsKey(first))!.data[0]).toMatchObject({
      id: 'a',
      status: 'Suspended',
      isLive: false,
    });

    expect(queries.getQueryData(adminTenantKey('a'))).toMatchObject({
      id: 'a',
      status: 'Suspended',
    });

    expect(queries.getQueryData<Page>(adminTenantsKey(first))!.data[1]).toMatchObject({
      id: 'b',
      status: 'active',
    });
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

    expect(queries.getQueryData<Page>(adminTenantsKey(first))!.data.map((r) => r.isLive)).toEqual([
      false,
      false,
    ]);
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
