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

const row = (id: string, status: string, isLive: boolean): Record<string, unknown> => ({
  id,
  name: id,
  status,
  isLive,
});
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
