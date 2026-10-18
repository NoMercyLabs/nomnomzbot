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
import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, defineComponent, h } from 'vue';
import { RouterLink, createMemoryHistory, createRouter } from 'vue-router';

import { chatKey } from '@/api/chat';
import { memberKey } from '@/api/community';
import { activityKey, homeStatsKey } from '@/api/home';
import { communityListKey, userNotesKey, viewerProfileKey } from '@/api/people';
import { prefetchRouteData } from '@/api/prefetch';
import { lookup } from '@/lib/array';

import { useIntentPrefetch } from '../useIntentPrefetch';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

const ok = { ok: true, status: 200 } as const satisfies Answer['response'];
const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0));

const answers = {
  '/api/v{version}/channels/{channelId}/chat/messages': [{ id: 'm1', timestamp: '2026-10-16T10:00:00Z' }],
  '/api/v{version}/channels/{channelId}/chat/emotes': [{ id: 'e1' }],
  '/api/v{version}/channels/{channelId}/community': [{ id: '42' }],
  '/api/v{version}/channels/{channelId}/community/{userId}': {
    id: '42',
    username: 'viewer',
    internalUserId: 'g-42',
  },
  '/api/v{version}/channels/{channelId}/community/{userId}/profile': { id: 'g-42' },
  '/api/v{version}/channels/{channelId}/moderation/users/{userId}/notes': [{ id: 1 }],
  '/api/v{version}/dashboard/{channelId}/stats': { viewers: 3 },
  '/api/v{version}/dashboard/{channelId}/activity': [],
} as const satisfies Record<string, unknown>;

const stub = defineComponent({ render: () => h('div') });
const lazyChat = vi.fn<() => Promise<typeof stub>>(() => Promise.resolve(stub));

let queries: QueryClient;
let wrapper: VueWrapper;

async function mountFrame(): Promise<void> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app',
        name: 'dashboard-home',
        component: stub,
      },
      {
        path: '/app/chat',
        name: 'dashboard-chat',
        component: lazyChat,
      },
      {
        path: '/app/people',
        name: 'dashboard-people',
        component: stub,
      },
    ],
  });

  await router.push('/app');
  await router.isReady();

  const Host = defineComponent({
    setup() {
      useIntentPrefetch();

      return () => h('div', [
        h(RouterLink, { to: { name: 'dashboard-chat' } }, () => 'Chat'),
        h(RouterLink, { to: { name: 'dashboard-people' } }, () => 'People'),
        h('button', { 'data-user': '42' }, 'viewer'),
        h('a', { href: 'https://example.com/x' }, 'outside'),
      ]);
    },
  });

  wrapper = mount(Host, {
    attachTo: document.body,
    global: { plugins: [router, [VueQueryPlugin, { queryClient: queries }]] },
  });
}

function point(selector: string, type: string = 'pointerover'): void {
  wrapper.find(selector).element.dispatchEvent(new MouseEvent(type, { bubbles: true }));
}

describe('useIntentPrefetch', () => {
  beforeEach(async () => {
    vi.clearAllMocks();

    http.GET.mockImplementation((path: string) =>
      Promise.resolve({
        data: { data: lookup(answers, path) },
        response: ok,
      }));

    queries = new QueryClient();
    await mountFrame();
  });

  afterEach(() => {
    wrapper.unmount();
  });

  it('puts the chat history and emotes in the cache when the pointer reaches the chat link', async () => {
    expect(queries.getQueryData(chatKey('c1'))).toBeUndefined();

    point('a[href="/app/chat"]');
    await settle();

    const history = queries.getQueryData<Array<{ id: string }>>(chatKey('c1'));

    expect(history?.map((line) => line.id)).toEqual(['m1']);
    expect(queries.getQueryData([...chatKey('c1'), 'emotes'])).toEqual([{ id: 'e1' }]);
  });

  it('loads the code chunk of the page the link opens', async () => {
    point('a[href="/app/chat"]');
    await settle();

    expect(lazyChat).toHaveBeenCalledTimes(1);
  });

  it('reacts to focus and touch the same way as to the pointer', async () => {
    point('a[href="/app/people"]', 'focusin');
    await settle();

    expect(queries.getQueryData(communityListKey('c1', 'all', ''))).toBeDefined();

    queries.clear();
    point('a[href="/app/chat"]', 'touchstart');
    await settle();

    expect(queries.getQueryData(chatKey('c1'))).toBeDefined();
  });

  it('puts the member, the viewer profile and the notes in the cache when the pointer reaches a name', async () => {
    point('button[data-user="42"]');
    await settle();

    expect(queries.getQueryData<{ username: string }>(memberKey('c1', '42'))?.username).toBe('viewer');
    expect(queries.getQueryData(viewerProfileKey('c1', 'g-42'))).toEqual({ id: 'g-42' });
    expect(queries.getQueryData(userNotesKey('c1', '42'))).toEqual([{ id: 1 }]);
  });

  it('does not ask again for an entry that is still fresh', async () => {
    point('a[href="/app/chat"]');
    await settle();

    const before = http.GET.mock.calls.length;

    point('a[href="/app/chat"]');
    await settle();

    expect(http.GET.mock.calls.length).toBe(before);
  });

  it('ignores a link that leaves the app', async () => {
    point('a[href="https://example.com/x"]');
    await settle();

    expect(http.GET).not.toHaveBeenCalled();
  });

  it('warms the home page with its reads', async () => {
    await prefetchRouteData(queries, 'dashboard-home', 'c1');

    expect(queries.getQueryData(homeStatsKey('c1'))).toEqual({ viewers: 3 });
    expect(queries.getQueryData(activityKey('c1'))).toEqual([]);
  });
});
