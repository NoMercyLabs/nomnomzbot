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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { createApp, type App } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { configDomainKeys } from '@/realtime/configDomains';

import { personalityKey, usePersonality } from '../personalitySettings';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}));

vi.mock('../client', () => ({ client: http }));

const body = { personality: 'friendly', available: ['informative', 'friendly'] };

function mountQuery(channelId: string | null) {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  let hook!: ReturnType<typeof usePersonality>;
  const app: App = createApp({
    setup() {
      hook = usePersonality(channelId);

      return () => null;
    },
  });

  app.use(createPinia());
  app.use(VueQueryPlugin, { queryClient: queries });
  app.mount(document.createElement('div'));

  return { queries, hook } as const satisfies {
    queries: QueryClient;
    hook: ReturnType<typeof usePersonality>;
  };
}

beforeEach(() => {
  http.GET.mockReset();
});

describe('usePersonality', () => {
  it('reads the personality of the channel and caches it under its key', async () => {
    http.GET.mockResolvedValue({ data: { data: body }, response: { ok: true, status: 200 } });

    const { queries, hook } = mountQuery('c1');

    await flushPromises();

    expect(http.GET).toHaveBeenCalledWith(
      '/api/v{version}/channels/{channelId}/settings/personality',
      { params: { path: { version: '1', channelId: 'c1' } } },
    );

    expect(hook.data.value).toEqual(body);
    expect(queries.getQueryData(personalityKey('c1'))).toEqual(body);
  });

  it('turns a refused read into an error that names the status', async () => {
    http.GET.mockResolvedValue({ error: { status: 'error' }, response: { ok: false, status: 403 } });

    const { hook } = mountQuery('c1');

    await flushPromises();

    expect(hook.error.value?.message).toBe('personality settings 403');
  });

  it('waits for a channel before it asks the server', async () => {
    const { hook } = mountQuery(null);

    await flushPromises();

    expect(http.GET).not.toHaveBeenCalled();
    expect(hook.fetchStatus.value).toBe('idle');
  });

  it('is marked stale by the channel-settings change the server publishes', () => {
    const builders = configDomainKeys['channel-settings']!;
    const keys = builders.map((build) => build('c1'));

    expect(keys).toContainEqual(personalityKey('c1'));
  });
});
