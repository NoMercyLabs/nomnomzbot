// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { createApp, type App } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
  networkBlockOptInKey,
  useNetworkBlockOptIn,
  useSaveNetworkBlockOptIn,
} from '../networkBlockOptIn';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('../client', () => ({ client: http }));

const channel = 'c1';
const ok = { ok: true, status: 200 };
const path = '/api/v{version}/channels/{channelId}/moderation/network-block-opt-in';

function mount<T>(use: () => T): { queries: QueryClient; hook: T } {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  let hook!: T;
  const app: App = createApp({
    setup() {
      hook = use();

      return () => null;
    },
  });

  app.use(VueQueryPlugin, { queryClient: queries });
  app.mount(document.createElement('div'));

  return { queries, hook };
}

const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0));

beforeEach(() => {
  http.GET.mockReset();
  http.PUT.mockReset();
});

describe('useNetworkBlockOptIn', () => {
  it('reads the stored answer from the moderation path', async () => {
    http.GET.mockResolvedValue({ data: { data: { accepts: true } }, response: ok });

    const { hook } = mount(() => useNetworkBlockOptIn(channel));

    await settle();

    expect(hook.data.value).toEqual({ accepts: true });

    const [calledPath, init] = http.GET.mock.calls[0] as [string, { params: { path: unknown } }];

    expect(calledPath).toBe(path);
    expect(init.params.path).toEqual({ version: '1', channelId: channel });
  });

  it('turns a refused read into a query error', async () => {
    http.GET.mockResolvedValue({ error: { detail: 'no' }, response: { ok: false, status: 403 } });

    const { hook } = mount(() => useNetworkBlockOptIn(channel));

    await settle();

    expect(hook.isError.value).toBe(true);
  });
});

describe('useSaveNetworkBlockOptIn', () => {
  it('sends the answer and refetches the stored value', async () => {
    http.PUT.mockResolvedValue({ response: { ok: true, status: 204 } });
    http.GET.mockResolvedValue({ data: { data: { accepts: true } }, response: ok });
    http.GET.mockResolvedValueOnce({ data: { data: { accepts: false } }, response: ok });

    const { queries, hook } = mount(() => ({
      read: useNetworkBlockOptIn(channel),
      save: useSaveNetworkBlockOptIn(channel),
    }));

    await settle();

    expect(queries.getQueryData(networkBlockOptInKey(channel))).toEqual({ accepts: false });

    await hook.save.mutateAsync({ accepts: true });
    await settle();

    const [calledPath, init] = http.PUT.mock.calls[0] as [string, { body: unknown }];

    expect(calledPath).toBe(path);
    expect(init.body).toEqual({ accepts: true });
    expect(queries.getQueryData(networkBlockOptInKey(channel))).toEqual({ accepts: true });
  });

  it('throws the bot reason on a refusal and leaves the cache alone', async () => {
    http.PUT.mockResolvedValue({
      error: { detail: 'Only the broadcaster may change this.' },
      response: { ok: false, status: 403 },
    });

    const { queries, hook } = mount(() => useSaveNetworkBlockOptIn(channel));

    queries.setQueryData(networkBlockOptInKey(channel), { accepts: false });

    await expect(hook.mutateAsync({ accepts: true })).rejects.toThrow('Only the broadcaster may change this.');

    expect(queries.getQueryData(networkBlockOptInKey(channel))).toEqual({ accepts: false });
  });
});
