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
import { createApp, ref, type App } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useChannelAccess, type ChannelAccess } from '../access';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};
type Call = [path: string, init: { params?: { path?: Record<string, unknown> } }];

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('../client', () => ({ client: http }));

const ok = { ok: true, status: 200 } as const satisfies Answer['response'];
const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0));

function held(keys: string[]): Answer {
  return {
    data: { data: {
      userId: 'u1',
      broadcasterId: 'c1',
      effectiveLevel: 'Moderator',
      communityStanding: 'Viewer',
      communityLevel: 'Viewer',
      managementRole: 'Moderator',
      managementLevel: 'Moderator',
      permitRole: null,
      permitCapabilities: [],
      winningSource: 'management',
      heldActionKeys: keys,
    } },
    response: ok,
  };
}

function mount(channelId: () => string | null): ChannelAccess {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  let hook!: ChannelAccess;
  const app: App = createApp({
    setup() {
      hook = useChannelAccess(channelId);

      return () => null;
    },
  });

  app.use(VueQueryPlugin, { queryClient: queries });
  app.mount(document.createElement('div'));

  return hook;
}

beforeEach(() => {
  http.GET.mockReset();
});

describe('useChannelAccess', () => {
  it('asks the bot what this person may do in the chosen channel', async () => {
    http.GET.mockResolvedValue(held([]));
    mount(() => 'c1');
    await settle();

    const [path, init] = http.GET.mock.calls[0] as Call;

    expect(path).toBe('/api/v{version}/channels/{channelId}/roles/effective/me');
    expect(init.params?.path).toEqual({ version: '1', channelId: 'c1' });
  });

  it('allows exactly the action keys the person holds', async () => {
    http.GET.mockResolvedValue(held(['chat:read', 'moderation:timeout']));
    const access = mount(() => 'c1');

    await settle();

    expect(access.known.value).toBe(true);
    expect(access.can('chat:read')).toBe(true);
    expect(access.can('moderation:timeout')).toBe(true);
    expect(access.can('commands:write')).toBe(false);
  });

  it('allows nothing while the answer is unknown, so no control offers a save that cannot work', async () => {
    http.GET.mockReturnValue(new Promise<Answer>(() => undefined));
    const access = mount(() => 'c1');

    await settle();

    expect(access.known.value).toBe(false);
    expect(access.can('chat:read')).toBe(false);
  });

  it('allows nothing when the bot refuses the question', async () => {
    http.GET.mockResolvedValue({ error: {}, response: { ok: false, status: 403 } });
    const access = mount(() => 'c1');

    await settle();

    expect(access.can('chat:read')).toBe(false);
  });

  it('asks again for the new channel when the channel changes', async () => {
    http.GET.mockImplementation((_path, init) => {
      const id = (init as Call[1]).params?.path?.channelId;

      return Promise.resolve(held(id === 'c2'
        ? ['commands:write']
        : []));
    });

    const channel = ref<string | null>('c1');
    const access = mount(() => channel.value);

    await settle();
    expect(access.can('commands:write')).toBe(false);

    channel.value = 'c2';
    await settle();
    expect(access.can('commands:write')).toBe(true);
  });
});
