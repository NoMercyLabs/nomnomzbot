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

import { configDomainKeys } from '@/realtime/configDomains';

import { basicsKey, useBasics, useUpdateBasics, type ChannelBasics } from '../settings';

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
const stored: ChannelBasics = {
  prefix: '!',
  botLinePrefix: null,
  locale: 'en',
  autoJoin: true,
  timezone: 'Europe/Amsterdam',
  keepActiveWhileOffline: false,
  viewerCasualFrom: 10,
  viewerRegularFrom: 100,
  viewerVeteranFrom: 500,
};

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

describe('useBasics', () => {
  it('reads the basics of the channel from the settings path', async () => {
    http.GET.mockResolvedValue({ data: { data: stored }, response: { ok: true, status: 200 } });

    const { hook } = mount(() => useBasics(channel));

    await settle();

    expect(hook.data.value).toEqual(stored);

    const [path, init] = http.GET.mock.calls[0] as [string, { params: { path: unknown } }];

    expect(path).toBe('/api/v{version}/channels/{channelId}/settings/basics');
    expect(init.params.path).toEqual({ version: '1', channelId: channel });
  });

  it('turns a refused read into a query error', async () => {
    http.GET.mockResolvedValue({ error: { detail: 'no' }, response: { ok: false, status: 403 } });

    const { hook } = mount(() => useBasics(channel));

    await settle();

    expect(hook.isError.value).toBe(true);
  });
});

describe('useUpdateBasics', () => {
  it('sends the change and keeps what the bot echoed in the cache', async () => {
    const echoed: ChannelBasics = { ...stored, prefix: '?' };

    http.PUT.mockResolvedValue({ data: { data: echoed }, response: { ok: true, status: 200 } });

    const { queries, hook } = mount(() => useUpdateBasics(channel));

    await hook.mutateAsync({ prefix: '?', autoJoin: true });

    const [, init] = http.PUT.mock.calls[0] as [string, { body: unknown }];

    expect(init.body).toEqual({ prefix: '?', autoJoin: true });
    expect(queries.getQueryData(basicsKey(channel))).toEqual(echoed);
  });

  it('throws the bot reason on a refusal and leaves the cache alone', async () => {
    http.PUT.mockResolvedValue({
      error: { detail: 'Prefix must not contain spaces.' },
      response: { ok: false, status: 400 },
    });

    const { queries, hook } = mount(() => useUpdateBasics(channel));

    queries.setQueryData(basicsKey(channel), stored);

    await expect(hook.mutateAsync({ prefix: 'a b' })).rejects.toThrow('Prefix must not contain spaces.');

    expect(queries.getQueryData(basicsKey(channel))).toEqual(stored);
  });
});

describe('live sync', () => {
  it('marks the basics stale when the channel settings change', () => {
    const builders = configDomainKeys['channel-settings'] ?? [];

    expect(builders.map((build) => build(channel))).toContainEqual(basicsKey(channel));
  });
});
