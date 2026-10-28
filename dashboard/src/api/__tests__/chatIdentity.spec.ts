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
import { createApp, type App } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
  chatIdentityKey,
  useChatIdentity,
  useSetChatColor,
  type ChatIdentity,
} from '../chatIdentity';

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

const ok = { ok: true, status: 200 } as const satisfies Answer['response'];
const channel = 'c1';

const identity: ChatIdentity = {
  displayName: 'Stoney_Eagle',
  color: '#1E90FF',
  canManageColor: true,
  colorScope: 'user:manage:chat_color',
  namedColors: [{ name: 'blue', hex: '#0000FF' }],
  badges: [{
    setId: 'broadcaster',
    id: '1',
    urls: { 1: 'https://img/1' },
  }],
};

const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0));

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

beforeEach(() => {
  http.GET.mockReset();
  http.PUT.mockReset();
});

describe('useChatIdentity', () => {
  it('reads the identity of the channel from the chat-identity route', async () => {
    http.GET.mockResolvedValue({ data: { data: identity }, response: ok });

    const { hook } = mount(() => useChatIdentity(channel));

    await settle();

    const [path, init] = http.GET.mock.calls[0] as [string, { params: { path: unknown } }];

    expect(path).toBe('/api/v{version}/channels/{channelId}/chat-identity');
    expect(init.params.path).toEqual({ version: '1', channelId: channel });
    expect(hook.data.value).toEqual(identity);
  });
});

describe('useSetChatColor', () => {
  it('sends the chosen color in the PUT body and caches the identity the server returns', async () => {
    const saved: ChatIdentity = { ...identity, color: '#0000FF' };

    http.PUT.mockResolvedValue({ data: { data: saved }, response: ok });

    const { queries, hook } = mount(() => useSetChatColor(channel));

    await hook.mutateAsync('blue');

    const [path, init] = http.PUT.mock.calls[0] as [
      string,
      { params: { path: unknown }; body: unknown },
    ];

    expect(path).toBe('/api/v{version}/channels/{channelId}/chat-identity/color');
    expect(init.params.path).toEqual({ version: '1', channelId: channel });
    expect(init.body).toEqual({ color: 'blue' });
    expect(queries.getQueryData<ChatIdentity>(chatIdentityKey(channel))).toEqual(saved);
  });

  it('keeps the cached identity and surfaces the reason when Twitch refuses the color', async () => {
    http.PUT.mockResolvedValue({
      error: {
        status: 'error',
        message: 'Turbo needed',
        code: 'FORBIDDEN',
      },
      response: { ok: false, status: 403 },
    });

    const { queries, hook } = mount(() => useSetChatColor(channel));

    queries.setQueryData(chatIdentityKey(channel), identity);

    await expect(hook.mutateAsync('#123456')).rejects.toThrow('Turbo needed');
    expect(queries.getQueryData<ChatIdentity>(chatIdentityKey(channel))).toEqual(identity);
  });
});
