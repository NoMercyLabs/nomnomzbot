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

import { eventFeedFactsQuery } from '../twitchHealth';

type Init = { params: { query?: { Page: number; PageSize: number } } };

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: Init) => Promise<object>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

const ok = { ok: true, status: 200 };
const subscriptionsPath = '/api/v{version}/channels/{channelId}/eventsub/subscriptions';

beforeEach(() => {
  http.GET.mockReset();
});

describe('the event feed facts', () => {
  it('reads every page of subscriptions, so a broken one past the first page is still judged', async () => {
    http.GET.mockImplementation((path, init) => {
      if (path !== subscriptionsPath)
        return Promise.resolve({ data: { data: {} }, response: ok });

      const first = init.params.query?.Page !== 2;

      return Promise.resolve({
        data: {
          data: [{ id: first
            ? 'early'
            : 'late' }],
          hasMore: first,
        },
        response: ok,
      });
    });

    const facts = await new QueryClient().query(eventFeedFactsQuery('channel-1'));

    expect(facts.subscriptions.map((row) => row.id)).toEqual(['early', 'late']);

    expect(
      http.GET.mock.calls
        .filter(([path]) => path === subscriptionsPath)
        .map(([, init]) => init.params.query),
    ).toEqual([
      { Page: 1, PageSize: 100 },
      { Page: 2, PageSize: 100 },
    ]);
  });
});
