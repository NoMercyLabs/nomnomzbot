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
import { createApp, ref, type App, type Ref } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useGiveawayEntries, useGiveawayWinners } from '../giveaways';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}));

vi.mock('../client', () => ({ client: http }));

const ok: Answer = { data: { data: [] }, response: { ok: true, status: 200 } };

function mountBoth(giveawayId: Ref<string | null>): App {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const app: App = createApp({
    setup() {
      useGiveawayWinners('c1', giveawayId);
      useGiveawayEntries('c1', giveawayId);

      return () => null;
    },
  });

  app.use(VueQueryPlugin, { queryClient: queries });
  app.mount(document.createElement('div'));

  return app;
}

function pathsFor(suffix: string): unknown[] {
  return http.GET.mock.calls
    .filter(([path]) => path.endsWith(suffix))
    .map(([, init]) => (init as { params: { path: { id: string } } }).params.path.id);
}

beforeEach(() => {
  http.GET.mockReset();
  http.GET.mockResolvedValue(ok);
});

describe('giveaway winners and entries', () => {
  it('send no request while no giveaway is chosen, then one each with the real id', async () => {
    const giveawayId = ref<string | null>(null);

    mountBoth(giveawayId);
    await flushPromises();

    expect(pathsFor('/winners')).toEqual([]);
    expect(pathsFor('/entries')).toEqual([]);

    giveawayId.value = 'g1';
    await flushPromises();

    expect(pathsFor('/winners')).toEqual(['g1']);
    expect(pathsFor('/entries')).toEqual(['g1']);
  });
});
