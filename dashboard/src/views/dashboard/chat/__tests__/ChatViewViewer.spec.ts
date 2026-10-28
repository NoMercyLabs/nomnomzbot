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
import type { Editor } from '@tiptap/core';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import type { ChatLine } from '@/api/chat';
import { Toaster } from '@/components/ui/sonner';
import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';
import appWords from '@/i18n/locales/en.json';

import ChatView from '../ChatView.vue';
import en from '../i18n/en.json';

type Options = { body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
};
type Answer = { data?: unknown; response: { status: number; ok: boolean } };

Range.prototype.getClientRects = () =>
  [] as unknown as DOMRectList;

Range.prototype.getBoundingClientRect = () =>
  new DOMRect();

const calls: Call[] = [];
let history: ChatLine[] = [];

const line = (id: string, name: string, message: string): ChatLine =>
  ({
    id,
    channelId: 'c1',
    userId: `u-${id}`,
    displayName: name,
    username: name.toLowerCase(),
    message,
    fragments: [{ type: 'text', text: message }],
    userType: '',
    isSubscriber: false,
    isVip: false,
    isModerator: false,
    isBroadcaster: false,
    isCheer: false,
    isCommand: false,
    badges: [],
    bitsAmount: 0,
    color: '#ff69b4',
    messageType: 'text',
    replyToMessageId: null,
    replyParentMessageBody: null,
    replyParentUserName: null,
    timestamp: '2026-10-16T18:10:00Z',
    provider: 'twitch',
  }) as unknown as ChatLine;

const nothing: Answer = { data: { data: null }, response: { status: 200, ok: true } };

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({
        method: 'GET',
        path,
        body: undefined,
      });

      return path.endsWith('/chat/viewer/emotes')
        ? { data: { data: [] }, response: { status: 200, ok: true } }
        : { data: { data: history }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: options?.body,
      });

      return { data: { data: null }, response: { status: 200, ok: true } };
    },
    PUT: async (): Promise<Answer> => nothing,
    DELETE: async (): Promise<Answer> => nothing,
  },
}));

// A participant holds the viewer chat keys and no moderation key.
vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: { value: true },
    can: (key: string): boolean => key.startsWith('chat:viewer:'),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref('c1'),
    current: ref({
      login: 'stoney_eagle',
      displayName: 'Stoney_Eagle',
      isLive: true,
    }),
  }),
}));

// The hub is not under test here: its live lines are proven in realtime/__tests__/viewerChat.spec.ts.
vi.mock('@/realtime/useViewerChatLive', () => ({ useViewerChatLive: () => undefined }));

let mounted: VueWrapper | null = null;

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/viewer-chat',
        name: 'dashboard-viewer-chat',
        component: { template: '<div />' },
        meta: { viewerChat: true },
      },
    ],
  });

  await router.push('/app/viewer-chat');

  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const framed = defineComponent(() => () => h(TooltipProvider, () => [h(ChatView), h(Toaster)]));

  mounted = mount(framed, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), router, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-line-id]'),
];

beforeEach(() => {
  calls.length = 0;
  localStorage.clear();
  setActivePinia(createPinia());
  history = [line('m1', 'Alice', 'first words'), line('m2', 'Bob', 'second words')];
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('en', appWords);

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );

  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(40);
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(4000);
  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(600);
  HTMLElement.prototype.scrollTo = function (): void {} as typeof HTMLElement.prototype.scrollTo;
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('the viewer chat page', () => {
  it('reads the viewer history and shows its lines in order', async () => {
    await open();

    expect(rows().map((row) => row.dataset.lineId)).toEqual(['m1', 'm2']);
    expect(calls.some((call) => call.method === 'GET' && call.path.endsWith('/chat/viewer/messages'))).toBe(true);
    expect(calls.some((call) => call.path.endsWith('/chat/messages'))).toBe(false);
  });

  it('shows no moderation, profile or identity control on a line or in the header', async () => {
    await open();

    const row = rows()[0]!;

    for (const name of ['timeout', 'delete', 'ban', 'profile']) {
      expect(row.querySelector(`[data-line-action="${name}"]`)).toBeNull();
    }

    expect(document.body.querySelector('[data-mod-tools-toggle]')).toBeNull();
    expect(document.body.querySelector('[data-identity-toggle]')).toBeNull();
    expect(document.body.querySelector('[data-mod-tools-panel]')).toBeNull();
  });

  it('sends a typed message to the viewer endpoint with no sender identity', async () => {
    await open();

    const editor = (document.body.querySelector('[data-composer]') as HTMLElement & { editor: Editor }).editor;

    editor.commands.setContent('hello chat');
    await settle();

    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    await settle();

    const posts = calls.filter((call) => call.method === 'POST');

    expect(posts.map((call) => call.path)).toEqual([
      '/api/v{version}/channels/{channelId}/chat/viewer/messages',
    ]);

    expect(posts[0]!.body).toMatchObject({ message: 'hello chat' });
    expect(posts[0]!.body).not.toHaveProperty('senderIdentity');
  });
});
