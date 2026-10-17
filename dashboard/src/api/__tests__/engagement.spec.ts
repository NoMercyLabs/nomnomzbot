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
import { flushPromises } from '@vue/test-utils';
import { createApp, type App } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
  engagementConfigKey,
  useEngagementConfig,
  useSaveEngagementConfig,
  type EngagementChange,
} from '../engagement';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}));

vi.mock('../client', () => ({ client: http }));

const row = {
  firstTimeChatterEnabled: true,
  returningChatterEnabled: false,
  watchStreakEnabled: true,
  streakMilestones: [5, '10'],
  greetCooldownSeconds: '30',
};

function mount<T>(
  channelId: string | null,
  use: (id: string | null) => T,
): { queries: QueryClient; hook: T } {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  let hook!: T;
  const app: App = createApp({
    setup() {
      hook = use(channelId);

      return () => null;
    },
  });

  app.use(VueQueryPlugin, { queryClient: queries });
  app.mount(document.createElement('div'));

  return { queries, hook };
}

beforeEach(() => {
  http.GET.mockReset();
  http.PUT.mockReset();
});

describe('useEngagementConfig', () => {
  it('reads the config from its own path and turns string numbers into numbers', async () => {
    http.GET.mockResolvedValue({ data: { data: row }, response: { ok: true, status: 200 } });

    const { queries, hook } = mount('c1', useEngagementConfig);

    await flushPromises();

    expect(http.GET).toHaveBeenCalledWith('/api/v{version}/engagement/config', {
      params: { path: { version: '1' } },
    });

    expect(hook.data.value).toEqual({
      firstTimeChatterEnabled: true,
      returningChatterEnabled: false,
      watchStreakEnabled: true,
      streakMilestones: [5, 10],
      greetCooldownSeconds: 30,
    });

    expect(queries.getQueryData(engagementConfigKey('c1'))).toEqual(hook.data.value);
  });

  it('turns a refused read into an error that names the status', async () => {
    http.GET.mockResolvedValue({ error: { status: 'error' }, response: { ok: false, status: 403 } });

    const { hook } = mount('c1', useEngagementConfig);

    await flushPromises();

    expect(hook.isError.value).toBe(true);
    expect(hook.error.value?.message).toBe('engagement config 403');
  });

  it('waits for a channel before it asks the server', async () => {
    const { hook } = mount(null, useEngagementConfig);

    await flushPromises();

    expect(http.GET).not.toHaveBeenCalled();
    expect(hook.fetchStatus.value).toBe('idle');
  });
});

describe('useSaveEngagementConfig', () => {
  const change: EngagementChange = {
    firstTimeChatterEnabled: false,
    returningChatterEnabled: true,
    watchStreakEnabled: true,
    streakMilestones: [3, 7],
    greetCooldownSeconds: 60,
  };

  it('sends the whole config and marks the cached config stale', async () => {
    http.PUT.mockResolvedValue({ data: { data: row }, response: { ok: true, status: 200 } });

    const { queries, hook } = mount('c1', useSaveEngagementConfig);

    queries.setQueryData(engagementConfigKey('c1'), { old: true });
    await hook.mutateAsync(change);

    expect(http.PUT).toHaveBeenCalledWith('/api/v{version}/engagement/config', {
      params: { path: { version: '1' } },
      body: change,
    });

    expect(queries.getQueryState(engagementConfigKey('c1'))?.isInvalidated).toBe(true);
  });

  it('keeps the cache and throws when the bot refuses', async () => {
    http.PUT.mockResolvedValue({
      error: { detail: 'Streak milestones must be positive.' },
      response: { ok: false, status: 400 },
    });

    const { queries, hook } = mount('c1', useSaveEngagementConfig);

    queries.setQueryData(engagementConfigKey('c1'), { old: true });

    await expect(hook.mutateAsync(change)).rejects.toThrow('Streak milestones must be positive.');
    expect(queries.getQueryState(engagementConfigKey('c1'))?.isInvalidated).toBe(false);
  });
});
