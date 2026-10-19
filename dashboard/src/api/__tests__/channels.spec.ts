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

import { moderatedChannelsQuery, myChannelsQuery } from '../channels';

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<unknown>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

const tooMany = {
  data: undefined,
  error: { status: 429, title: 'Too many requests' },
  response: { ok: false, status: 429 },
};

function freshClient(): QueryClient {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

beforeEach(() => {
  http.GET.mockReset();
});

describe('the channel lists a refused request', () => {
  it('fails the own channel list on a 429 instead of answering an empty list', async () => {
    http.GET.mockResolvedValue(tooMany);

    await expect(freshClient().fetchQuery(myChannelsQuery)).rejects.toBeInstanceOf(Error);
  });

  it('fails the moderated channel list on a 429 instead of answering an empty list', async () => {
    http.GET.mockResolvedValue(tooMany);

    await expect(freshClient().fetchQuery(moderatedChannelsQuery)).rejects.toBeInstanceOf(Error);
  });

  it('still answers the rows of a good response', async () => {
    http.GET.mockResolvedValue({
      data: { data: [{ id: 'own-id', login: 'streamer' }] },
      response: { ok: true, status: 200 },
    });

    const rows = await freshClient().fetchQuery(myChannelsQuery);

    expect(rows).toEqual([{ id: 'own-id', login: 'streamer' }]);
  });
});
