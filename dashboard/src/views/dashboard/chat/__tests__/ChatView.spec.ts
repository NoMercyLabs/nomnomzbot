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
import type { Editor } from '@tiptap/core';
import type { Transaction } from '@tiptap/pm/state';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { chatKey, type ChatEmoteEntry, type ChatLine } from '@/api/chat';
import { Toaster } from '@/components/ui/sonner';
import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';
import appWords from '@/i18n/locales/en.json';
import { useProfileDialogStore } from '@/stores/profileDialog';

import ChatView from '../ChatView.vue';
import en from '../i18n/en.json';

type Options = {
  params?: { path?: Record<string, unknown>; query?: Record<string, unknown> };
  body?: unknown;
};
type Call = {
  method: string;
  path: string;
  body: unknown;
  messageId: string;
  query?: Record<string, unknown>;
};
type Answer = { data?: unknown; response: { status: number; ok: boolean } };
type SearchPage = { messages: ChatLine[]; nextCursor: string | null };

// jsdom lays nothing out; the send box editor asks where the caret is when it takes the focus.
Range.prototype.getClientRects = () =>
  [] as unknown as DOMRectList;

Range.prototype.getBoundingClientRect = () =>
  new DOMRect();

const calls: Call[] = [];
let history: ChatLine[] = [];
let sendOk: boolean = true;
// Who is signed in; the channel belongs to stoney_eagle.
let signedInAs: string = 'stoney_eagle';
// The search answer per `before` cursor; the first page has no cursor. A cursor with no page fails.
let searchPages: Record<string, SearchPage> = {};

let favoriteList: Record<string, unknown>[] = [];

const emoteList = ['bigCat', 'catKISS', 'catJAM', 'Kappa'].map((code): ChatEmoteEntry => ({
  id: `id-${code}`,
  origin: 'channel',
  code,
  provider: 'Twitch',
  urls: { 1: `https://cdn.test/${code}-1.webp`, 2: `https://cdn.test/${code}-2.webp` },
  animated: false,
  zeroWidth: false,
  setId: null,
}));

const searchAnswer = (options?: Options): Answer => {
  const page = searchPages[String(options?.params?.query?.before ?? '')];

  return page === undefined
    ? { response: { status: 500, ok: false } }
    : { data: { data: page }, response: { status: 200, ok: true } };
};

// The community record of the chatter the Viewer panel asks for.
const memberOf = (options?: Options): Record<string, unknown> => {
  const id = String(options?.params?.path?.userId ?? '');

  return {
    id,
    internalUserId: null,
    username: `user-${id}`,
    displayName: `User ${id}`,
    profileImageUrl: null,
    messageCount: 1,
    watchHours: 0,
    commandsUsed: 0,
    trustLevel: 'viewer',
    isBanned: false,
    firstSeen: null,
    lastSeen: null,
  };
};

const line = (
  id: string,
  userId: string,
  name: string,
  message: string,
  over: object = {},
): ChatLine =>
  ({
    id,
    channelId: 'c1',
    userId,
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
    timestamp: '2026-10-12T18:10:00Z',
    provider: 'twitch',
    ...over,
  }) as unknown as ChatLine;

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'GET',
        path,
        body: undefined,
        messageId: '',
        query: options?.params?.query,
      });

      if (path.endsWith('/chat/search'))
        return searchAnswer(options);

      if (path.endsWith('/chat/emotes'))
        return { data: { data: emoteList }, response: { status: 200, ok: true } };

      if (path.endsWith('/favorite-emotes'))
        return { data: { data: [...favoriteList] }, response: { status: 200, ok: true } };

      if (path.endsWith('/community/{userId}'))
        return { data: { data: memberOf(options) }, response: { status: 200, ok: true } };

      if (path.endsWith('/auth/me')) {
        return { data: { data: { username: signedInAs } }, response: { status: 200, ok: true } };
      }

      return { data: { data: history }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: options?.body,
        messageId: '',
      });

      const ok = !path.endsWith('/chat/messages') || sendOk;

      return { data: { data: null }, response: { status: ok
        ? 200
        : 500, ok } };
    },
    PUT: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'PUT',
        path,
        body: options?.body,
        messageId: '',
      });

      favoriteList.unshift({ ...(options?.body as object), createdAt: '2026-10-14T00:00:00Z' });

      return { data: { data: null }, response: { status: 200, ok: true } };
    },
    DELETE: async (path: string, options?: Options): Promise<Answer> => {
      const messageId = String(options?.params?.path?.messageId);

      calls.push({
        method: 'DELETE',
        path,
        body: undefined,
        messageId,
      });

      const star = options?.params?.path as { provider?: string; emoteId?: string } | undefined;

      favoriteList = favoriteList.filter((favorite) =>
        favorite.provider !== star?.provider || favorite.emoteId !== star?.emoteId);

      return { data: { data: null }, response: { status: 200, ok: true } };
    },
  },
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

const ROW_HEIGHT: number = 40;
const VIEWPORT_HEIGHT: number = 4000;

// jsdom has no layout: the feed is VIEWPORT_HEIGHT tall and every line is ROW_HEIGHT tall.
function stubLayout(): void {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );

  vi.stubGlobal(
    'IntersectionObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );

  const sizeOf = (element: Element): number =>
    element.hasAttribute('data-virtual-scroll')
      ? VIEWPORT_HEIGHT
      : ROW_HEIGHT;

  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this);
  });

  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this);
  });

  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(600);

  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    return {
      width: 600,
      height: sizeOf(this),
      top: 0,
      left: 0,
      right: 600,
      bottom: sizeOf(this),
      x: 0,
      y: 0,
    } as DOMRect;
  });

  HTMLElement.prototype.scrollTo = function (
    this: HTMLElement,
  ): void {} as typeof HTMLElement.prototype.scrollTo;
}

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;
let queries: QueryClient;

beforeEach(() => {
  calls.length = 0;
  localStorage.clear();
  sendOk = true;
  signedInAs = 'stoney_eagle';
  searchPages = {};

  history = [
    line('m1', 'u1', 'Alice', 'first words'),
    line('m2', 'u2', 'Bob', 'second words'),
    line('m3', 'u1', 'Alice', '!sr some song', { isCommand: true }),
  ];

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
  stubLayout();
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(path = '/app/chat'): Promise<void> {
  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/chat',
        name: 'dashboard-chat',
        component: { template: '<div />' },
      },
      {
        path: '/popout/chat',
        name: 'popout-chat',
        component: { template: '<div />' },
        meta: { popout: true },
      },
      {
        path: '/app/settings',
        name: 'dashboard-settings',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push(path);
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // The page runs inside the frame's tooltip provider and toast host, so the test gives it the same.
  const framed = defineComponent(() => () => h(TooltipProvider, () => [h(ChatView), h(Toaster)]));

  mounted = mount(framed, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, router, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-line-id]'),
];
const rowIds = (): string[] => rows().map((row) => row.dataset.lineId ?? '');
const rowOf = (id: string): HTMLElement =>
  rows().find((row) => row.dataset.lineId === id) as HTMLElement;
const action = (id: string, name: string): HTMLButtonElement =>
  rowOf(id).querySelector(`[data-line-action="${name}"]`) as HTMLButtonElement;

// jsdom has no pointer capture, so the menu is opened the keyboard way (Enter on the trigger).
const openMenu = (trigger: HTMLElement): void => {
  trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
};

// The send box is a TipTap editor; TipTap puts the editor on its element.
const input = (): HTMLElement & { editor: Editor } =>
  document.body.querySelector('[data-composer]') as HTMLElement & { editor: Editor };
// The text the box would send: an emote image counts as its code.
const typed = (): string =>
  input().editor.getText();
const writes = (): Call[] => calls.filter((call) => call.method !== 'GET');

// Types into an emptied box one key at a time, the way a browser hands keys to the editor.
async function type(value: string): Promise<void> {
  const { view } = input().editor;

  input().editor.commands.clearContent(true);

  for (const key of value) {
    const { from, to } = view.state.selection;
    const insert = (): Transaction =>
      view.state.tr.insertText(key, from, to);
    const handled = view.someProp('handleTextInput', (handler) =>
      handler(view, from, to, key, insert));

    if (!handled) {
      view.dispatch(insert());
    }
  }

  await settle();
}

async function send(): Promise<void> {
  document.body
    .querySelector('form')!
    .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

  await settle();
}

describe('ChatView', () => {
  it('pops the chat out into its own window', async () => {
    const popup = vi.spyOn(window, 'open').mockReturnValue(null);

    await open();
    document.body.querySelector<HTMLButtonElement>('[data-chat-popout]')!.click();

    expect(popup).toHaveBeenCalledWith('/popout/chat', 'nnz-chat', 'popup,width=440,height=760');
    popup.mockRestore();
  });

  it('copies the full dock address for an OBS browser dock', async () => {
    const writeText = vi.fn<(text: string) => Promise<void>>(async () => undefined);

    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await open();
    document.body.querySelector<HTMLButtonElement>('[data-copy-link]')!.click();
    await flushPromises();

    expect(writeText).toHaveBeenCalledWith(`${location.origin}/popout/chat`);
  });

  it('offers no Pop out and no dock link inside the popped-out window', async () => {
    await open('/popout/chat');

    expect(rows().length).toBeGreaterThan(0);
    expect(document.body.querySelector('[data-chat-popout]')).toBeNull();
    expect(document.body.querySelector('[data-copy-link]')).toBeNull();
  });

  it('packs the popped-out rows and tray tight so the tray fits a 320px window', async () => {
    await open('/popout/chat');

    const row = rows()[0]!;
    const tray: HTMLElement = row.querySelector('[data-tray]')!;
    const SIZE_ICON_PX = 36;
    const WINDOW_PX = 320;
    const buttons: number = tray.querySelectorAll('[data-line-action]').length;

    expect(row.className).toContain('px-2');
    expect(row.className).not.toContain('px-3');
    expect(tray.className).toContain('right-1');
    expect(tray.className).not.toContain('right-3');
    expect(buttons * SIZE_ICON_PX).toBeLessThan(WINDOW_PX);
  });

  it('keeps the roomy rows and tray in the full chat page', async () => {
    await open();

    const row = rows()[0]!;

    expect(row.className).toContain('px-3');
    expect(row.querySelector('[data-tray]')!.className).toContain('right-3');
  });

  it('renders the history lines in order with their names and messages', async () => {
    await open();
    expect(rowIds()).toEqual(['m1', 'm2', 'm3']);
    expect(rowOf('m2').textContent).toContain('Bob');
    expect(rowOf('m2').textContent).toContain('second words');
  });

  // Stoney 2026-10-14: a chatter allowed to use HTML sees it rendered, the same as on the overlay. The
  // server sanitizes an html fragment; the dashboard purifies it again before it touches the DOM.
  it('renders an html fragment as markup and strips anything that could run', async () => {
    const markup: string =
      '<marquee><img src="https://cdn.test/dino.gif" alt="DinoDance" class="emote" onerror="alert(1)"></marquee><script>alert(2)</script>';

    history = [line('m1', 'u1', 'Geen', 'raw', { fragments: [{ type: 'html', text: markup }] })];
    await open();

    const html: Element = rowOf('m1').querySelector('[data-html]')!;

    expect(html.querySelector('marquee img.emote')?.getAttribute('alt')).toBe('DinoDance');
    expect(html.querySelector('img')?.hasAttribute('onerror')).toBe(false);
    expect(html.querySelector('script')).toBeNull();
    expect(rowOf('m1').textContent).not.toContain('<marquee>');
  });

  // Owner 2026-10-14: "i want images posted to be expandable in a popout dialog modal". An emote is not a posted image.
  it('opens a posted image large in a dialog and leaves html emotes alone', async () => {
    const markup: string =
      '<img class="emote" src="https://cdn.test/dino.gif" alt="DinoDance"><img src="https://cdn.test/photo.png" alt="A photo">';

    history = [line('m1', 'u1', 'Geen', 'raw', { fragments: [{ type: 'html', text: markup }] })];
    await open();

    const html: Element = rowOf('m1').querySelector('[data-html]')!;

    html.querySelector<HTMLImageElement>('img.emote')!.click();
    await flushPromises();

    expect(document.body.querySelector('[data-lightbox-image]')).toBeNull();

    html.querySelector<HTMLImageElement>('img:not(.emote)')!.click();
    await flushPromises();

    const big: HTMLImageElement | null = document.body.querySelector('[data-lightbox-image]');

    expect(big?.getAttribute('src')).toBe('https://cdn.test/photo.png');
    expect(big?.getAttribute('alt')).toBe('A photo');

    document.body.querySelector<HTMLElement>('[data-slot="dialog-content"]')!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }),
    );

    await flushPromises();

    expect(document.body.querySelector('[data-lightbox-image]')).toBeNull();
  });

  // Stoney 2026-10-14: "make them frankerfacez style": a mention is bold text in the mentioned chatter's color,
  // never a pill with a background that blends into the row highlight.
  it('shows a mention as bold text in the mentioned chatter color, with no background', async () => {
    history = [
      line('m1', 'u1', 'Bamo', '@iFiLLz Cool', {
        fragments: [
          {
            type: 'mention',
            text: '@iFiLLz',
            mention: {
              userId: 'u9',
              username: 'ifillz',
              displayName: 'iFiLLz',
              color: '#ff7f50',
            },
          },
          { type: 'text', text: ' Cool' },
        ],
      }),
    ];

    await open();

    const tag: HTMLElement = rowOf('m1').querySelector('[data-mention-tag]')!;

    expect(tag.textContent).toBe('@iFiLLz');
    expect(tag.className).toContain('font-bold');
    expect(tag.className).not.toMatch(/\bbg-|\bring-|\bpx-/);
    expect(tag.style.color).not.toBe('');
  });

  // Imported history carries a mention as plain text; it still reads as a mention.
  it('bolds an @name written in plain text, the same as a mention fragment', async () => {
    history = [line('m1', 'u1', 'Geen', 'thanks @f0xb17 for that')];
    await open();

    const tags: string[] = [...rowOf('m1').querySelectorAll('[data-mention-tag]')].map((tag: Element) => tag.textContent ?? '');

    expect(tags).toEqual(['@f0xb17']);
    expect(rowOf('m1').querySelector('[data-mention-tag]')!.className).toContain('font-bold');
  });

  it('shows a line that is pushed into the feed', async () => {
    await open();

    queries.setQueryData<ChatLine[]>(chatKey('c1'), (feed) => [
      ...(feed ?? []),
      line('m4', 'u3', 'Cara', 'just arrived'),
    ]);

    await settle();
    expect(rowIds()).toEqual(['m1', 'm2', 'm3', 'm4']);
    expect(rowOf('m4').textContent).toContain('just arrived');
  });

  it.each([
    [{ removed: 'timeout', timeoutSeconds: 600 }, 'Timed out · 10 min'],
    [{ removed: 'timeout', timeoutSeconds: 45 }, 'Timed out · 45 sec'],
    [{ removed: 'timeout', timeoutSeconds: 1_209_600 }, 'Timed out · 14 days'],
    [{ removed: 'banned' }, 'Banned'],
  ] as const)('a removed line says what happened: %o', async (mark, said) => {
    await open();

    queries.setQueryData<ChatLine[]>(chatKey('c1'), (feed) =>
      feed?.map((one) =>
        one.id === 'm1'
          ? { ...one, ...mark }
          : one),
    );

    await settle();
    expect(rowOf('m1').querySelector('[data-removal]')?.textContent?.trim()).toBe(said);
    expect(rowOf('m2').querySelector('[data-removal]')).toBeNull();
  });

  it('strikes a deleted line, and Delete calls DELETE with that line id', async () => {
    await open();

    queries.setQueryData<ChatLine[]>(chatKey('c1'), (feed) =>
      feed?.map((one) =>
        one.id === 'm1'
          ? { ...one, removed: 'deleted' }
          : one),
    );

    await settle();
    expect(rowOf('m1').dataset.removed).toBe('deleted');
    expect(rowOf('m2').dataset.removed).toBeUndefined();

    action('m2', 'delete').click();
    await settle();
    expect(writes()).toEqual([expect.objectContaining({ method: 'DELETE', messageId: 'm2' })]);
  });

  it('shows five tray buttons in order: reply, timeout, delete, ban, profile', async () => {
    await open();
    const names = [...rowOf('m2').querySelectorAll<HTMLElement>('[data-line-action]')].map(
      (button) => button.dataset.lineAction,
    );

    expect(names).toEqual(['reply', 'timeout', 'delete', 'ban', 'profile']);
  });

  it('keeps every tray button a plain ghost icon with a 20px icon, harm only in the ban confirm', async () => {
    await open();

    for (const name of ['reply', 'timeout', 'delete', 'ban', 'profile']) {
      const button = action('m2', name);

      expect(button.dataset.variant).toBe('ghost');
      expect(button.dataset.size).toBe('icon');
      expect(button.querySelector('svg')!.classList.contains('size-5')).toBe(true);
    }
  });

  it('opens the profile of the line person from the profile button', async () => {
    await open();
    const store = useProfileDialogStore(pinia);

    action('m2', 'profile').click();
    await settle();
    expect(store.userId).toBe('u2');
  });

  it('opens a length menu on Timeout and sends nothing until a length is picked', async () => {
    await open();
    openMenu(action('m2', 'timeout'));
    await settle();
    const items = [...document.body.querySelectorAll<HTMLElement>('[data-timeout-length]')].map(
      (item) => item.dataset.timeoutLength ?? '',
    );

    expect(items).toEqual(['60', '600', '3600', '86400']);
    expect(writes()).toEqual([]);
  });

  it('times out the person for the picked length', async () => {
    await open();
    openMenu(action('m2', 'timeout'));
    await settle();
    document.body.querySelector<HTMLElement>('[data-timeout-length="3600"]')!.click();
    await settle();
    expect(writes()).toHaveLength(1);
    expect(writes()[0]!.path).toBe('/api/v{version}/channels/{channelId}/moderation/actions');

    expect(writes()[0]!.body).toEqual({
      action: 'timeout',
      targetUserId: 'u2',
      durationSeconds: 3600,
      reason: null,
    });
  });

  describe('who may be timed out or banned from the tray', () => {
    const names = (id: string): string[] =>
      [...rowOf(id).querySelectorAll<HTMLElement>('[data-line-action]')].map(
        (button) => button.dataset.lineAction ?? '',
      );

    const sanctions = ['timeout', 'ban'];

    beforeEach(() => {
      history = [
        line('v1', 'u1', 'Alice', 'a viewer'),
        line('mod1', 'u7', 'Modest', 'another mod', { isModerator: true }),
        line('lead1', 'u8', 'Leader', 'a lead mod', {
          badges: [{
            setId: 'lead_moderator',
            id: '1',
            urls: {},
          }],
        }),
        line('own1', 'u9', 'Stoney_Eagle', 'the broadcaster', { isBroadcaster: true }),
        line('me1', 'u5', 'Helper', 'my own line', { isModerator: true }),
      ];
    });

    it('lets a moderator sanction a viewer', async () => {
      signedInAs = 'helper';
      await open();
      expect(names('v1')).toEqual(expect.arrayContaining(sanctions));
    });

    it('hides Timeout and Ban for a moderator on their own line, keeping Delete', async () => {
      signedInAs = 'helper';
      await open();
      expect(names('me1')).toEqual(['reply', 'delete', 'profile']);
    });

    it('hides Timeout and Ban for a moderator on the broadcaster line', async () => {
      signedInAs = 'helper';
      await open();
      expect(names('own1')).toEqual(['reply', 'delete', 'profile']);
    });

    it('hides Timeout and Ban for a moderator on another moderator, and on a lead moderator', async () => {
      signedInAs = 'helper';
      await open();
      expect(names('mod1')).toEqual(['reply', 'delete', 'profile']);
      expect(names('lead1')).toEqual(['reply', 'delete', 'profile']);
    });

    it('lets the broadcaster sanction a moderator and a lead moderator', async () => {
      await open();
      expect(names('mod1')).toEqual(expect.arrayContaining(sanctions));
      expect(names('lead1')).toEqual(expect.arrayContaining(sanctions));
    });

    it('hides Timeout and Ban for the broadcaster on their own line', async () => {
      await open();
      expect(names('own1')).toEqual(['reply', 'delete', 'profile']);
    });
  });

  it('has no length filter in the panel header', async () => {
    await open();
    expect(document.body.querySelector('[data-slot="filter-bar"]')).toBeNull();
  });

  it('shows the character counter only past 400 characters', async () => {
    await open();
    await type('x'.repeat(400));
    expect(document.body.querySelector('[data-composer-count]')).toBeNull();
    await type('x'.repeat(401));

    expect(document.body.querySelector('[data-composer-count]')?.textContent?.trim()).toBe(
      '401/500',
    );
  });

  it('bans only after the confirm, in this channel', async () => {
    await open();
    action('m2', 'ban').click();
    await settle();
    expect(writes()).toEqual([]);
    const confirm = document.body.querySelector('[data-confirm-action]') as HTMLButtonElement;

    confirm.click();
    await settle();
    expect(writes()).toHaveLength(1);
    expect(writes()[0]!.path).toBe('/api/v{version}/channels/{channelId}/moderation/actions/ban');

    expect(writes()[0]!.body).toEqual({
      targetTwitchUserId: 'u2',
      scope: 'this_channel',
      reason: null,
    });
  });

  it('sends the typed message as you, and clears the box', async () => {
    await open();
    await type('hello chat');
    await send();
    expect(writes()).toHaveLength(1);
    expect(writes()[0]!.path).toBe('/api/v{version}/channels/{channelId}/chat/messages');

    expect(writes()[0]!.body).toEqual({
      message: 'hello chat',
      senderIdentity: 'you',
      replyToMessageId: null,
    });

    expect(typed()).toBe('');
  });

  it('blocks a message over 500 characters and says why', async () => {
    await open();
    await type('x'.repeat(501));
    await send();
    expect(writes()).toEqual([]);

    expect(document.body.textContent).toContain(
      en.dashboard.chat.composer.tooLong.replace('{max}', '500'),
    );

    expect(document.body.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(
      true,
    );
  });

  // Command lines are a view option: the Commands switch in the Display drawer.
  async function switchOffCommands(): Promise<void> {
    document.body.querySelector<HTMLElement>('[data-display-toggle]')!.click();
    await settle();

    document.body
      .querySelector<HTMLElement>('[data-display-panel] [data-option="commands"] [role="switch"]')!
      .click();

    await settle();
  }

  it('puts the page controls in the control bar under the title, not in the feed card', async () => {
    await open();
    const bar = document.body.querySelector('[data-slot="page-bar"]')!;

    expect(bar.querySelector('[data-display-toggle]')).not.toBeNull();

    expect(document.body.querySelector('[role="switch"]')).toBeNull();
  });

  // Stoney 2026-10-13: "searching messages should also be possible and go all the way back from the
  // first message to return chat messages containing a fuzzy search result".
  describe('search', () => {
    const searchBox = (): HTMLInputElement =>
      document.body.querySelector('[data-slot="page-bar"] input[type="search"]') as HTMLInputElement;
    const searches = (): Call[] => calls.filter((call) => call.path.endsWith('/chat/search'));

    async function search(value: string): Promise<void> {
      searchBox().value = value;
      searchBox().dispatchEvent(new Event('input', { bubbles: true }));
      await new Promise((done) => setTimeout(done, 300));
      await settle();
    }

    beforeEach(() => {
      searchPages = {
        '': {
          messages: [line('s9', 'u2', 'Bob', 'hello again'), line('s8', 'u1', 'Alice', 'helo there')],
          nextCursor: 'older',
        },
        older: { messages: [line('s1', 'u1', 'Alice', 'hello first')], nextCursor: null },
      };
    });

    it('asks the server for the typed words and shows only the matching lines, newest first', async () => {
      await open();
      await search('hello');

      expect(searches()).toHaveLength(1);
      expect(searches()[0]!.query?.q).toBe('hello');
      expect(rowIds()).toEqual(['s9', 's8']);
      expect(document.body.querySelector('[data-composer]')).toBeNull();
    });

    it('goes further back with the cursor until the first message, then says so', async () => {
      await open();
      await search('hello');

      document.body.querySelector<HTMLButtonElement>('[data-search-more]')!.click();
      await settle();

      expect(searches().at(-1)!.query?.before).toBe('older');
      expect(rowIds()).toEqual(['s9', 's8', 's1']);
      expect(document.body.querySelector('[data-search-more]')).toBeNull();

      expect(document.body.querySelector('[data-search-end]')?.textContent).toContain(
        'Searched back to the first message',
      );
    });

    it('says a failed search failed, and never that it reached the first message', async () => {
      searchPages = {};
      await open();
      await search('hello');

      expect(document.body.querySelector('[data-slot="panel-error"]')?.textContent).toContain(
        'The search did not work.',
      );

      expect(document.body.querySelector('[data-search-end]')).toBeNull();
    });

    it('returns to the live feed when the search box is cleared', async () => {
      await open();
      await search('hello');
      await search('');

      expect(rowIds()).toEqual(['m1', 'm2', 'm3']);
      expect(document.body.querySelector('[data-composer]')).not.toBeNull();
    });
  });

  it('removes command lines while the Commands view option is off', async () => {
    await open();
    expect(rowIds()).toContain('m3');
    await switchOffCommands();
    expect(rowIds()).toEqual(['m1', 'm2']);
  });

  it('remembers the Commands view option after the page is closed and opened again', async () => {
    await open();
    await switchOffCommands();
    mounted?.unmount();
    mounted = null;
    await open();
    expect(rowIds()).toEqual(['m1', 'm2']);

    expect(JSON.parse(localStorage.getItem('nnz.chat.display')!)).toMatchObject({
      commands: false,
    });
  });

  it('shows the error of a failed send and keeps the text', async () => {
    sendOk = false;
    await open();
    await type('will not go');
    await send();
    expect(writes()).toHaveLength(1);

    expect(document.body.querySelector('[role="alert"]')?.textContent).toContain(
      en.dashboard.chat.composer.failed,
    );

    expect(typed()).toBe('will not go');
  });

  it('opens the profile of the person on the line', async () => {
    await open();
    const store = useProfileDialogStore(pinia);

    rowOf('m2').querySelector<HTMLElement>('[data-slot="user-name"]')!.click();
    await settle();
    expect(store.userId).toBe('u2');
  });
});

describe('ChatView display drawer', () => {
  const badge = {
    setId: 'subscriber',
    id: '1',
    info: null,
    urls: { 1: 'https://cdn.test/sub.png' },
  };
  const emote = {
    urls: { 1: 'https://cdn.test/kappa.png' },
    animated: false,
    format: 'static',
    zeroWidth: false,
  };

  beforeEach(() => {
    history = [
      line('m1', 'u1', 'Alice', 'first words', {
        badges: [
          badge,
          {
            setId: 'campaign-1-ranking',
            id: '1',
            info: null,
            urls: {},
          },
        ],
        avatarUrl: 'https://cdn.test/alice.png',
        color: '#0000ff',
        fragments: [
          { type: 'text', text: 'first words ' },
          {
            type: 'emote',
            text: 'Kappa',
            emote,
          },
        ],
      }),
      line('m2', 'u2', 'Bob', 'hey @stoney_eagle look', {
        removed: 'deleted',
        fragments: [{ type: 'text', text: 'hey @stoney_eagle look' }],
      }),
      line('m3', 'u3', 'Cara', 'third words'),
    ];
  });

  const toggle = (): HTMLButtonElement =>
    document.body.querySelector('[data-display-toggle]') as HTMLButtonElement;
  const panel = (): HTMLElement | null => document.body.querySelector('[data-display-panel]');
  const option = (name: string): HTMLElement =>
    panel()!.querySelector(`[data-option="${name}"]`) as HTMLElement;

  async function showPanel(): Promise<void> {
    toggle().click();
    await settle();
  }

  async function flip(name: string): Promise<void> {
    option(name).querySelector<HTMLElement>('[role="switch"]')!.click();
    await settle();
  }

  async function pick(name: string, label: string): Promise<void> {
    const buttons = [...option(name).querySelectorAll<HTMLElement>('button')];

    buttons.find((button) => button.textContent?.trim() === label)!.click();
    await settle();
  }

  const stored = (): Record<string, unknown> =>
    JSON.parse(localStorage.getItem('nnz.chat.display') ?? '{}') as Record<string, unknown>;
  const display = en.dashboard.chat.display;

  it('opens and closes the drawer from the header button', async () => {
    await open();
    expect(panel()).toBeNull();
    expect(toggle().getAttribute('aria-pressed')).toBe('false');
    await showPanel();
    expect(panel()).not.toBeNull();
    expect(toggle().getAttribute('aria-pressed')).toBe('true');
    await showPanel();
    expect(panel()).toBeNull();
  });

  it('says the choice is kept on this device, never on the account', async () => {
    await open();
    await showPanel();
    expect(panel()!.textContent).toContain(display.kept);
    expect(panel()!.textContent).not.toMatch(/account/i);
  });

  it('starts with the mockup defaults', async () => {
    await open();
    expect(rowOf('m1').querySelector('time')?.textContent).not.toMatch(/AM|PM/i);
    expect(rowOf('m1').querySelector('time')?.textContent).toMatch(/\d{2}:\d{2}/);

    expect(
      [...rowOf('m1').querySelectorAll('img[data-badge]')].map((img) => img.getAttribute('src')),
    ).toEqual(['https://cdn.test/sub.png']);

    expect(rowOf('m1').querySelector('img[data-avatar]')).toBeNull();
    expect(rowOf('m1').querySelector('svg[data-platform]')).toBeNull();
    expect(rowOf('m1').className).not.toContain('max-w-[65ch]');
    expect(rowOf('m1').style.getPropertyValue('--emote-h')).toBe('1.5em');
    expect(rowOf('m2').querySelector('p')!.className).toContain('line-through');
    expect(rowOf('m2').querySelector('mark[data-mention]')?.textContent).toBe('@stoney_eagle');
  });

  it('timestamps: 12-hour shows AM or PM, off removes the time', async () => {
    await open();
    await showPanel();
    await pick('timestamps', display.hour12);
    expect(rowOf('m1').querySelector('time')?.textContent).toMatch(/AM|PM/i);
    await pick('timestamps', display.off);
    expect(rowOf('m1').querySelector('time')).toBeNull();
    expect(stored().timestamps).toBe('off');
  });

  it('badges, avatars and platform icons each add or remove their mark', async () => {
    await open();
    await showPanel();
    await flip('badges');
    expect(rowOf('m1').querySelector('img[data-badge]')).toBeNull();
    await flip('avatars');

    expect(rowOf('m1').querySelector<HTMLImageElement>('img[data-avatar]')?.src).toBe(
      'https://cdn.test/alice.png',
    );

    expect(rowOf('m3').querySelector('img[data-avatar]')).toBeNull();
    await flip('platform');
    expect(rowOf('m1').querySelector('svg[data-platform]')).not.toBeNull();
  });

  it('alternating rows mark every second visible row', async () => {
    await open();
    await showPanel();
    await flip('alternating');
    expect(rows().map((row) => row.dataset.alt)).toEqual(['off', 'on', 'off']);
  });

  it('name colors: as sent keeps the color, readable lifts it, off drops it', async () => {
    await open();
    await showPanel();

    const nameColor = (): string =>
      rowOf('m1').querySelector<HTMLElement>('[data-slot="user-name"]')!.style.color;

    const readable = nameColor();

    await pick('nameColors', display.asSent);
    expect(nameColor()).toBe('rgb(0, 0, 255)');
    expect(readable).not.toBe('rgb(0, 0, 255)');
    expect(readable).not.toBe('');
    await pick('nameColors', display.off);
    expect(nameColor()).toBe('');
  });

  it('narrow lines cap the line width', async () => {
    await open();
    await showPanel();
    await flip('narrow');
    expect(rowOf('m1').className).toContain('max-w-[65ch]');
  });

  it('message spacing changes the vertical padding of a line', async () => {
    await open();
    await showPanel();
    await pick('spacing', display.tight);
    expect(rowOf('m1').className).toContain('py-0.5');
    await pick('spacing', display.roomy);
    expect(rowOf('m1').className).toContain('py-4');
  });

  it('rows switch puts the time and name above the message, and is kept', async () => {
    await open();
    await showPanel();

    const head = (): HTMLElement => rowOf('m1').querySelector('[data-line-head]') as HTMLElement;

    expect(rowOf('m1').dataset.rows).toBe('one');
    expect(head().className).toContain('inline-flex');
    expect(head().querySelector('time')).toBeNull();
    expect(rowOf('m1').querySelector(':scope > time')).not.toBeNull();

    await pick('rows', display.twoLines);
    expect(rowOf('m1').dataset.rows).toBe('two');
    expect(head().className).not.toContain('inline-flex');
    expect(head().querySelector('time')).not.toBeNull();
    expect(rowOf('m1').querySelector(':scope > time')).toBeNull();
    expect(head().textContent).toContain('Alice');
    expect(head().textContent).not.toContain('first words');
    expect(rowOf('m1').textContent).toContain('first words');
    expect(stored().rows).toBe('two');
  });

  it('emote size sets the emote height', async () => {
    await open();
    await showPanel();

    const height = (): string => rowOf('m1').style.getPropertyValue('--emote-h');
    const lineHeight = (): string => rowOf('m1').style.getPropertyValue('--line-h');

    expect(rowOf('m1').querySelector('img[data-emote]')!.className).toContain('h-(--emote-h)');
    await pick('emoteSize', display.small);
    expect(height()).toBe('1.2em');
    await pick('emoteSize', display.large);
    expect(height()).toBe('2.3em');
    expect(lineHeight()).toBe('max(1.5rem, 2.3em)');
  });

  it('mention highlight can be switched off', async () => {
    await open();
    await showPanel();
    expect(rowOf('m2').dataset.mention).toBe('true');
    await flip('mentions');
    expect(rowOf('m2').querySelector('mark[data-mention]')).toBeNull();
    expect(rowOf('m2').dataset.mention).toBeUndefined();
  });

  it('deleted messages: show keeps the text readable and says deleted, hide removes the line', async () => {
    await open();
    await showPanel();
    await pick('deleted', display.show);
    expect(rowOf('m2').querySelector('p')!.className).not.toContain('line-through');
    expect(rowOf('m2').textContent).toContain(display.deletedTag);
    await pick('deleted', display.hide);
    expect(rowIds()).toEqual(['m1', 'm3']);
  });

  it('Reset puts every option back to its default', async () => {
    await open();
    await showPanel();
    await pick('timestamps', display.off);
    await flip('avatars');
    await pick('deleted', display.hide);
    expect(rowIds()).toEqual(['m1', 'm3']);
    panel()!.querySelector<HTMLElement>('[data-display-reset]')!.click();
    await settle();
    expect(rowIds()).toEqual(['m1', 'm2', 'm3']);
    expect(rowOf('m1').querySelector('time')).not.toBeNull();
    expect(rowOf('m1').querySelector('img[data-avatar]')).toBeNull();
  });

  it('keeps the choices after the page is opened again', async () => {
    await open();
    await showPanel();
    await pick('timestamps', display.off);
    await flip('narrow');
    mounted?.unmount();
    mounted = null;
    await open();
    expect(rowOf('m1').querySelector('time')).toBeNull();
    expect(rowOf('m1').className).toContain('max-w-[65ch]');
  });

  it('puts a 3-option segmented control under its label, full width, so nothing is cut', async () => {
    await open();
    await showPanel();

    const row = option('timestamps');
    const control = row.querySelector<HTMLElement>('[role="group"]')!;

    expect(row.className).toContain('flex-col');
    expect(control.className).toContain('w-full');
    expect(control.querySelectorAll('button')).toHaveLength(3);
  });

  it('links to the font and readability settings', async () => {
    await open();
    await showPanel();

    const link = panel()!.querySelector<HTMLAnchorElement>('a[data-display-readability]');

    expect(link?.getAttribute('href')).toBe('/app/settings');
  });

  describe('viewer focus', () => {
    beforeEach(() => {
      history = [
        line('m1', 'u1', 'Alice', 'first words'),
        line('m2', 'u2', 'Bob', 'second words'),
        line('m3', 'u1', 'Alice', 'third words'),
      ];
    });

    const viewer = (): HTMLElement | null =>
      document.body.querySelector<HTMLElement>('[data-viewer-panel]');

    it('opens the Viewer on the clicked chatter, lights their lines and dims the rest', async () => {
      await open();
      expect(viewer()).toBeNull();

      rowOf('m1').click();
      await settle();

      expect(viewer()?.textContent).toContain('Alice');
      expect(rowOf('m1').dataset.focus).toBe('on');
      expect(rowOf('m3').dataset.focus).toBe('on');
      expect(rowOf('m2').dataset.focus).toBe('dim');
    });

    it('tints the focused lines at 8% primary, never the full accent, and steps the rest back to 70%', async () => {
      await open();
      rowOf('m1').click();
      await settle();

      expect(rowOf('m1').className).toContain('bg-primary/8');
      expect(rowOf('m1').className).not.toContain('bg-accent');
      expect(rowOf('m2').className).toContain('opacity-70');
    });

    // Owner 2026-10-14: "i selected bamo and my name tag still draws my eyes".
    it('drops the mention tint of a dimmed line, so only the picked chatter stands out', async () => {
      history[1] = line('m2', 'u2', 'Bob', 'hey @stoney_eagle look');
      await open();
      expect(rowOf('m2').className).toContain('bg-primary/10');

      rowOf('m1').click();
      await settle();

      expect(rowOf('m2').dataset.focus).toBe('dim');
      expect(rowOf('m2').className).not.toContain('bg-primary/10');
    });

    it('offers Clear focus on the chatter in the header, and it ends the focus', async () => {
      await open();

      const clear = (): HTMLElement | null =>
        document.body.querySelector<HTMLElement>('[data-focus-clear]');

      expect(clear()).toBeNull();

      rowOf('m1').click();
      await settle();

      expect(clear()?.textContent).toContain('Alice');

      clear()?.click();
      await settle();

      expect(rowOf('m2').dataset.focus).toBeUndefined();
      expect(clear()).toBeNull();
    });

    it('lists in the Viewer only the lines the feed shows, so a hidden command stays out', async () => {
      localStorage.setItem('nnz.chat.display', JSON.stringify({ commands: false }));

      history = [
        line('m1', 'u1', 'Alice', 'first words'),
        line('m4', 'u1', 'Alice', '!sr some song', { isCommand: true }),
      ];

      await open();
      rowOf('m1').click();
      await settle();

      const listed = [...viewer()!.querySelectorAll('[data-viewer-message]')].map((item) =>
        item.textContent);

      expect(listed).toHaveLength(1);
      expect(listed[0]).toContain('first words');
    });

    it('ends the focus and closes the panel when the same chatter is picked again', async () => {
      await open();
      rowOf('m1').click();
      await settle();
      rowOf('m3').click();
      await settle();

      expect(rowOf('m2').dataset.focus).toBeUndefined();
      expect(viewer()).toBeNull();
    });

    it('ends the focus on Escape and closes everything with the close button', async () => {
      await open();
      rowOf('m2').click();
      await settle();
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      await settle();

      expect(rowOf('m1').dataset.focus).toBeUndefined();

      rowOf('m2').click();
      await settle();
      viewer()?.querySelector<HTMLElement>('[data-drawer-close]')?.click();
      await settle();

      expect(viewer()).toBeNull();
      expect(rowOf('m2').dataset.focus).toBeUndefined();
    });

    // Stoney 2026-10-13: the tray covered the end of a long message (right, centered). Stoney 2026-10-14:
    // "what is this weird hover placement" — above the name it covered the line above where the eye reads.
    // It sits at the row's top-right corner, above the row: never over this line, and over the quiet
    // right end of the line above, touching the row so the hover holds on the way up.
    it('floats the line actions at the top-right corner above the row, never over any name or this line', async () => {
      await open();
      const row = rowOf('m1');
      const tray: HTMLElement = row.querySelector('[data-tray]')!;

      // The row is the box the tray is placed against: no positioned wrapper sits between them.
      expect(tray.parentElement!.closest('.relative')).toBe(row);
      expect(tray.classList).toContain('bottom-full');
      expect(tray.classList).toContain('right-3');
      expect(tray.classList).not.toContain('left-0');
      expect(tray.classList).not.toContain('top-1/2');
      // Placed on the row's own top edge, it drops 2px into the row at every spacing, so no gap breaks the hover.
      expect(tray.classList).toContain('translate-y-0.5');
      expect([...tray.classList].some((name) => name.startsWith('-translate-y'))).toBe(false);
    });

    // Stoney 2026-10-14: "what is this weird behavior for the quick actions?" Trays stayed up on rows the mouse
    // had left: a tooltip trigger's data-state held the tray open. Only an open menu in the tray holds it.
    it('holds the tray open only for its open menu, never for a tooltip state', async () => {
      await open();
      const tray: HTMLElement = rowOf('m1').querySelector('[data-tray]')!;

      expect(tray.classList).not.toContain('has-data-[state=open]:opacity-100');
      expect(tray.classList).toContain('has-aria-expanded:opacity-100');
    });

    it('a click on a line action does not pick the chatter', async () => {
      await open();
      action('m1', 'profile').click();
      await settle();

      expect(viewer()).toBeNull();
      expect(useProfileDialogStore().userId).toBe('u1');
    });

    // A picked chatter opens the panel and its close button shuts it, so a header button adds nothing.
    it('has no Viewer button and shows the panel only while a chatter is picked', async () => {
      await open();

      expect(document.body.querySelector('[data-viewer-toggle]')).toBeNull();
      expect(viewer()).toBeNull();

      rowOf('m2').click();
      await settle();
      expect(viewer()).not.toBeNull();

      rowOf('m2').click();
      await settle();
      expect(viewer()).toBeNull();
    });
  });
});

describe('ChatView emote and badge cards', () => {
  type EmotePerson = {
    id: string;
    login: string;
    displayName: string;
    profileUrl: string | null;
  };
  type People = { owner?: EmotePerson; artist?: EmotePerson };

  const sevenTv: People & Record<string, unknown> = {
    id: '01F6ME',
    setId: null,
    provider: 'SevenTv',
    format: 'animated',
    animated: true,
    zeroWidth: true,
    urls: {
      1: 'https://cdn.test/catjam-1.webp',
      4: 'https://cdn.test/catjam-4.webp',
      2: 'https://cdn.test/catjam-2.webp',
    },
  };
  const twitch: People & Record<string, unknown> = {
    id: '25',
    setId: '0',
    provider: 'Twitch',
    format: 'static',
    animated: false,
    zeroWidth: false,
    urls: { 1: 'https://cdn.test/kappa-1.png', 3: 'https://cdn.test/kappa-3.png' },
  };

  beforeEach(() => {
    history = [
      line('m1', 'u1', 'Alice', 'catJAM Kappa', {
        badges: [
          {
            setId: 'subscriber',
            id: '12',
            info: '14',
            urls: { 1: 'https://cdn.test/sub-1.png', 2: 'https://cdn.test/sub-2.png' },
          },
        ],
        fragments: [
          {
            type: 'emote',
            text: 'catJAM',
            emote: sevenTv,
          },
          { type: 'text', text: ' ' },
          {
            type: 'emote',
            text: 'Kappa',
            emote: twitch,
          },
        ],
      }),
    ];
  });

  const card = (): HTMLElement | null => document.body.querySelector<HTMLElement>('[data-asset-card]');
  const factsOf = (shown: HTMLElement): string[] =>
    [...shown.querySelectorAll('dt, dd')].map((cell) => cell.textContent?.trim() ?? '');
  const asset = appWords.asset;

  async function press(selector: string): Promise<void> {
    rowOf('m1').querySelector(selector)!.closest('button')!.click();
    await settle();
  }

  it('opens a 7TV emote at its largest size with its name, source, kind, layer and its 7TV page', async () => {
    await open();
    await press('img[alt="catJAM"]');

    const shown = card()!;

    expect(shown.querySelector('img')!.getAttribute('src')).toBe('https://cdn.test/catjam-4.webp');
    expect(shown.textContent).toContain('catJAM');

    expect(factsOf(shown)).toEqual([
      asset.provider, '7TV',
      asset.kind, asset.animated,
      asset.layer, asset.zeroWidth,
    ]);

    const link = shown.querySelector<HTMLAnchorElement>('a')!;

    expect(link.href).toBe('https://7tv.app/emotes/01F6ME');
    expect(link.target).toBe('_blank');
    expect(link.rel).toContain('noopener');
  });

  it('offers no outside page for a Twitch emote, and says it is a still image', async () => {
    await open();
    await press('img[alt="Kappa"]');

    const shown = card()!;

    expect(shown.querySelector('img')!.getAttribute('src')).toBe('https://cdn.test/kappa-3.png');
    expect(factsOf(shown)).toEqual([asset.provider, 'Twitch', asset.kind, asset.still]);
    expect(shown.querySelector('a')).toBeNull();
  });

  it('names a Twitch emote\'s channel, and the name opens that person\'s profile', async () => {
    twitch.owner = {
      id: '12826',
      login: 'twitch',
      displayName: 'Twitch',
      profileUrl: null,
    };

    await open();
    await press('img[alt="Kappa"]');

    const owner = card()!.querySelector<HTMLButtonElement>('[data-asset-person="owner"]')!;

    expect(card()!.textContent).toContain(asset.people.channel);
    expect(owner.textContent?.trim()).toBe('Twitch');

    owner.click();
    await settle();

    expect(useProfileDialogStore().userId).toBe('12826');
    delete twitch.owner;
  });

  it('names a 7TV emote\'s uploader and artist, each linked to their own 7TV page', async () => {
    sevenTv.owner = {
      id: 'up1',
      login: 'uploader',
      displayName: 'Uploader',
      profileUrl: 'https://7tv.app/users/up1',
    };

    sevenTv.artist = {
      id: 'art1',
      login: 'artist',
      displayName: 'Artist',
      profileUrl: 'https://7tv.app/users/art1',
    };

    await open();
    await press('img[alt="catJAM"]');

    const person = (role: string): HTMLAnchorElement =>
      card()!.querySelector<HTMLAnchorElement>(`a[data-asset-person="${role}"]`)!;

    expect(card()!.textContent).toContain(asset.people.uploader);
    expect(card()!.textContent).toContain(asset.people.artist);
    expect(person('owner').href).toBe('https://7tv.app/users/up1');
    expect(person('owner').textContent?.trim()).toBe('Uploader');
    expect(person('artist').href).toBe('https://7tv.app/users/art1');
    expect(person('artist').rel).toContain('noopener');
    delete sevenTv.owner;
    delete sevenTv.artist;
  });

  it('opens a badge with its set, version and the detail Twitch sent, at the larger image', async () => {
    await open();
    await press('img[data-badge]');

    const shown = card()!;

    expect(shown.querySelector('img')!.getAttribute('src')).toBe('https://cdn.test/sub-2.png');
    expect(factsOf(shown)).toEqual([asset.set, 'subscriber', asset.version, '12', asset.info, '14']);
  });

  it('keeps the line small: the line shows the smallest image and a click on an emote picks no chatter', async () => {
    await open();
    expect(rowOf('m1').querySelector('img[alt="catJAM"]')!.getAttribute('src')).toBe('https://cdn.test/catjam-1.webp');

    await press('img[alt="catJAM"]');

    expect(rowOf('m1').dataset.focus).toBeUndefined();
    expect(document.body.querySelector('[data-viewer-panel]')).toBeNull();
  });
});

// Stoney 2026-10-14: "i need more twitch native features like reply". A reply names the line it answers in
// the composer, travels as replyToMessageId, and a reply line shows what it answers, as Twitch does.
describe('ChatView reply', () => {
  const reply = en.dashboard.chat.reply;
  const banner = (): HTMLElement | null => document.body.querySelector('[data-reply-banner]');

  async function replyTo(id: string): Promise<void> {
    action(id, 'reply').click();
    await settle();
  }

  it('names the answered line above the composer and moves the cursor into the box', async () => {
    await open();
    await replyTo('m2');

    expect(banner()!.textContent).toContain('Bob');
    expect(banner()!.textContent).toContain('second words');

    // TipTap moves the focus one animation frame later.
    await vi.waitFor(() => {
      expect(document.activeElement).toBe(input());
    });
  });

  it('sends the reply with the answered message id, then drops the banner', async () => {
    await open();
    await replyTo('m2');
    await type('hi bob');
    await send();

    expect(writes()[0]!.body).toEqual({
      message: 'hi bob',
      senderIdentity: 'you',
      replyToMessageId: 'm2',
    });

    expect(banner()).toBeNull();
  });

  it('cancels the reply with its button, so the next message is a plain one', async () => {
    await open();
    await replyTo('m2');
    document.body.querySelector<HTMLButtonElement>('[data-reply-cancel]')!.click();
    await settle();
    expect(banner()).toBeNull();

    await type('plain');
    await send();
    expect(writes()[0]!.body).toEqual(expect.objectContaining({ replyToMessageId: null }));
  });

  it('cancels the reply with Escape in the box', async () => {
    await open();
    await replyTo('m2');
    input().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(banner()).toBeNull();
  });

  // Stoney 2026-10-14: "escape does not exit focus to let me press / for search".
  it('leaves the box on Escape when there is nothing to cancel', async () => {
    await open();
    input().focus();
    expect(document.activeElement).toBe(input());

    input().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(document.activeElement).not.toBe(input());
  });

  // Stoney 2026-10-14: "i also want to escape search and shortcut to chat".
  it('leaves the search on Escape and jumps to the chat box on c', async () => {
    await open();
    const search: HTMLInputElement = document.querySelector('[data-slot="search-field"] input')!;

    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: '/', bubbles: true }));
    expect(document.activeElement).toBe(search);

    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(document.activeElement).not.toBe(search);

    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'c', bubbles: true }));
    await settle();
    expect(input().contains(document.activeElement)).toBe(true);
  });

  // Stoney 2026-10-14: "can you add the shortcut mark to chat so people know it?"
  it('marks the chat box with its key until you are in it', async () => {
    await open();
    const mark = document.querySelector<HTMLElement>('[data-composer-box] [data-shortcut-mark]');

    expect(mark?.textContent?.trim()).toBe('C');
    expect(mark!.classList).toContain('group-focus-within/box:hidden');
  });

  it('shows the parent a reply line answers, from the names Twitch sent', async () => {
    history = [
      line('m4', 'u2', 'Bob', '@Alice yes', {
        replyToMessageId: 'gone',
        replyParentUserName: 'Alice',
        replyParentMessageBody: 'is it live?',
      }),
    ];

    await open();

    const parent = rowOf('m4').querySelector('[data-reply-parent]')!;

    expect(parent.textContent).toContain('Alice');
    expect(parent.textContent).toContain('is it live?');
  });

  it('falls back to the loaded parent line when Twitch sent no parent names', async () => {
    history = [line('m1', 'u1', 'Alice', 'first words'), line('m5', 'u2', 'Bob', 'agreed', { replyToMessageId: 'm1' })];
    await open();

    const parent = rowOf('m5').querySelector('[data-reply-parent]')!;

    expect(parent.textContent).toContain('Alice');
    expect(parent.textContent).toContain('first words');
  });

  it('says it answers an earlier message when the parent is neither sent nor loaded', async () => {
    history = [line('m6', 'u2', 'Bob', 'agreed', { replyToMessageId: 'old' })];
    await open();
    expect(rowOf('m6').querySelector('[data-reply-parent]')!.textContent).toContain(reply.earlier);
  });

  it('shows no parent on a line that answers nothing', async () => {
    await open();
    expect(rowOf('m2').querySelector('[data-reply-parent]')).toBeNull();
  });
});

// Stoney 2026-10-14: "pick what the kmp version already does better and take it, like an emote picker and
// autocomplete". As in KMP and Twitch: ":" + 2 letters offers emotes and emoji, "@" offers the chatters here,
// the arrows move, Tab or Enter puts the pick in, Escape closes the list.
describe('ChatView composer suggestions', () => {
  const options = (): HTMLElement[] => [...document.body.querySelectorAll<HTMLElement>('[data-suggestion]')];
  const offered = (): string[] => options().map((option) => option.dataset.suggestion ?? '');

  const key = async (name: string): Promise<void> => {
    input().dispatchEvent(new KeyboardEvent('keydown', {
      key: name,
      bubbles: true,
      cancelable: true,
    }));

    await settle();
  };

  it('offers the emotes holding the letters, starts first, then shorter, after a colon and two letters', async () => {
    await open();
    await type('hi :ca');
    expect(offered().slice(0, 3)).toEqual(['catJAM', 'catKISS', 'bigCat']);
    expect(options()[0]!.querySelector('img')!.getAttribute('src')).toBe('https://cdn.test/catJAM-2.webp');
  });

  // Stoney 2026-10-14: "make sure inline also shows the emote".
  it('shows a known emote as its image once a space follows, and sends its code', async () => {
    await open();
    await type('hi Kappa Kappax ');

    const images = [...input().querySelectorAll<HTMLImageElement>('img[data-emote]')];

    expect(images.map((image) =>
      [image.dataset.emote, image.getAttribute('src')])).toEqual([['Kappa', 'https://cdn.test/Kappa-2.webp']]);

    expect(typed()).toBe('hi Kappa Kappax ');

    await key('Enter');
    expect(writes()).toHaveLength(1);
    expect((writes()[0]!.body as { message: string }).message).toBe('hi Kappa Kappax ');
    expect(typed()).toBe('');
  });

  it('pastes as plain text, with only known emote codes as images', async () => {
    await open();

    const paste = new Event('paste', { bubbles: true, cancelable: true }) as Event & {
      clipboardData: { getData: (kind: string) => string };
    };

    paste.clipboardData = {
      getData: (kind) =>
        kind === 'text/plain'
          ? 'one\ncatJAM <b>two</b>'
          : '<img src="https://evil.test/x.png" onerror="alert(1)"><b>two</b>',
    };

    input().dispatchEvent(paste);
    await settle();

    expect(typed()).toBe('one catJAM <b>two</b>');

    expect([...input().querySelectorAll('img')].map((image) =>
      image.getAttribute('src'))).toEqual(['https://cdn.test/catJAM-2.webp']);

    expect(input().querySelector('b')).toBeNull();
  });

  it('keeps the picker button inside the send box', async () => {
    await open();
    const box = document.body.querySelector('[data-composer-box]')!;

    expect(box.contains(input())).toBe(true);
    expect(box.querySelector('[data-emote-picker]')).not.toBeNull();
  });

  it('offers nothing after a colon and only one letter', async () => {
    await open();
    await type('hi :c');
    expect(options()).toEqual([]);
  });

  it('puts the chosen emote in with a space after the arrows and Tab, and sends nothing', async () => {
    await open();
    await type('hi :ca');
    await key('ArrowDown');
    expect(options()[1]!.getAttribute('aria-selected')).toBe('true');

    await key('Tab');
    expect(typed()).toBe('hi catKISS ');
    expect(options()).toEqual([]);
    expect(writes()).toEqual([]);
  });

  it('takes the highlighted pick on Enter instead of sending', async () => {
    await open();
    await type(':kap');
    await key('Enter');
    expect(typed()).toBe('Kappa ');
    expect(writes()).toEqual([]);
  });

  it('offers emoji by their short code and puts the emoji itself in', async () => {
    await open();
    await type(':smile');
    expect(offered()).toContain('😄');

    options().find((option) => option.dataset.suggestion === '😄')!.click();
    await settle();
    expect(typed()).toBe('😄 ');
  });

  it('offers the chatters in this chat after an at sign and puts the name in', async () => {
    await open();
    await type('thanks @al');
    expect(offered()).toEqual(['@Alice']);

    await key('Tab');
    expect(typed()).toBe('thanks @Alice ');
  });

  it('closes the list on Escape and keeps the reply that is open', async () => {
    await open();
    action('m2', 'reply').click();
    await settle();
    await type(':ca');
    await key('Escape');
    expect(options()).toEqual([]);
    expect(document.body.querySelector('[data-reply-banner]')).not.toBeNull();
  });

  // Stoney 2026-10-14: "no speak as bot option, can be abused to get mine banned".
  it('speaks only as you: no sender choice, and the emote list is yours', async () => {
    await open();
    expect(document.body.querySelector('[data-segmented]')).toBeNull();

    await type('hello');
    await send();
    expect(writes()[0]!.body).toEqual(expect.objectContaining({ senderIdentity: 'you' }));

    const asked = calls
      .filter((call) =>
        call.path.endsWith('/chat/emotes'))
      .map((call) =>
        call.query?.senderIdentity);

    expect(asked).toEqual(['you']);
  });
});

describe('ChatView emote picker', () => {
  const picker = (): HTMLElement | null => document.body.querySelector('[data-picker]');
  const sections = (): string[] => [...document.body.querySelectorAll<HTMLElement>('[data-picker-section]')]
    .map((section) =>
      section.dataset.pickerSection ?? '');
  const itemsOf = (id: string): string[] => [
    ...document.body.querySelectorAll<HTMLElement>(`[data-picker-section="${id}"] [data-picker-item]`),
  ].map((item) =>
    item.dataset.pickerItem ?? '');
  // jsdom's selector engine cannot match an emoji inside an attribute value, so the lookups compare the data.
  const item = (insert: string): HTMLElement =>
    [...document.body.querySelectorAll<HTMLElement>('[data-picker-item]')].find((found) =>
      found.dataset.pickerItem === insert)!;
  const skinOption = (glyph: string): HTMLElement =>
    [...document.body.querySelectorAll<HTMLElement>('[data-skin-option]')].find((found) =>
      found.dataset.skinOption === glyph)!;

  beforeEach(() => {
    localStorage.removeItem('nnz.chat.recent-picks');
    localStorage.removeItem('nnz.chat.skin-tone');
    localStorage.removeItem('nnz.chat.skin-tones');
    localStorage.removeItem('nnz.chat.picker-collapsed');
    favoriteList = [];
  });

  async function openPicker(): Promise<void> {
    (document.body.querySelector('[data-emote-picker]') as HTMLElement).click();
    await settle();
    await vi.dynamicImportSettled();
    await settle();
  }

  async function searchPicker(value: string): Promise<void> {
    const field = picker()!.querySelector('input') as HTMLInputElement;

    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  it('shows this channel\'s emotes, then the emoji per group, with a jump bar to each', async () => {
    await open();
    await openPicker();

    expect(sections()).toEqual([
      'channel', 'emoji-smileys', 'emoji-people', 'emoji-animals', 'emoji-food', 'emoji-travel',
      'emoji-activities', 'emoji-objects', 'emoji-symbols', 'emoji-flags',
    ]);

    expect(itemsOf('channel')).toEqual(['bigCat', 'catKISS', 'catJAM', 'Kappa']);
    expect(item('bigCat').querySelector('img')!.getAttribute('src')).toBe('https://cdn.test/bigCat-2.webp');

    const jumps = [...picker()!.querySelectorAll<HTMLElement>('[data-picker-jump]')].map((jump) =>
      jump.dataset.pickerJump);

    expect(jumps).toEqual(sections());
  });

  // Stoney 2026-10-14: "emote search input at the bottom, section buttons as icon only on the side of the emote
  // scroll list" and "autofocus on search input on opening the menu".
  it('opens with the cursor in the search below the list, and an icon rail beside it', async () => {
    await open();
    await openPicker();

    const search = picker()!.querySelector('[data-picker-query]') as HTMLInputElement;
    const list = picker()!.querySelector('[data-picker-section]')!;

    expect(document.activeElement).toBe(search);
    expect(list.compareDocumentPosition(search) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    const rail = [...picker()!.querySelectorAll<HTMLElement>('[data-picker-jump]')];

    expect(rail.every((jump) =>
      jump.textContent.trim() === '' || jump.textContent.trim().length <= 2)).toBe(true);

    expect(rail[0]!.querySelector('img')!.getAttribute('src')).toBe('https://cdn.test/bigCat-2.webp');
    expect(rail[0]!.getAttribute('aria-label')).toBe('Stoney_Eagle');
    expect(rail[0]!.getAttribute('aria-current')).toBe('true');
  });

  // Stoney 2026-10-14: "where did you leave the frankerfacez and the other tabs?"
  it('gives the FrankerFaceZ global emotes their own named section on the rail', async () => {
    emoteList.push({
      ...emoteList[0]!,
      id: 'id-LULW',
      code: 'LULW',
      provider: 'Ffz',
      origin: 'global',
    });

    try {
      await open();
      await openPicker();

      const jump = picker()!.querySelector('[data-picker-jump="global-Ffz"]');

      expect(itemsOf('global-Ffz')).toEqual(['LULW']);
      expect(jump?.getAttribute('aria-label')).toBe('FrankerFaceZ global');
    } finally {
      emoteList.pop();
    }
  });

  // Stoney 2026-10-14: "allow for favorites". Right-click, or a long press on a phone, stars an emote; the star
  // is saved for the person, and the starred emote leads the picker.
  it('stars an emote on a right-click, saves it, and lists it first under Favorites', async () => {
    await open();
    await openPicker();

    item('catJAM').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();

    const saved = calls.filter((call) =>
      call.method === 'PUT' && call.path.endsWith('/favorite-emotes'));

    expect(saved.map((call) =>
      call.body)).toEqual([{
      provider: 'Twitch',
      emoteId: 'id-catJAM',
      code: 'catJAM',
      imageUrl: 'https://cdn.test/catJAM-2.webp',
    }]);

    expect(sections()[0]).toBe('favorites');
    expect(itemsOf('favorites')).toEqual(['catJAM']);
    expect(typed()).toBe('');
  });

  // Stoney 2026-10-14: "right clicking an emote should unfavorite it, and i want to favorite all emotes".
  it('takes the star off on a second right-click', async () => {
    await open();
    await openPicker();

    item('catJAM').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();
    item('catJAM').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();

    const removed = calls.filter((call) =>
      call.method === 'DELETE' && call.path.includes('/favorite-emotes/'));

    expect(removed).toHaveLength(1);
    expect(sections()).not.toContain('favorites');
  });

  // Stoney 2026-10-14, on the skin tone popup: "i think we need a favorite button for these". Right-click on a
  // toned emoji asks for its tone, so the popup carries the star.
  it('stars an emoji with skin tones from the star in its tone popup', async () => {
    await open();
    await openPicker();

    item('👋').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();
    document.body.querySelector<HTMLElement>('[data-skin-tones] [data-tone-star]')!.click();
    await settle();

    expect(itemsOf('favorites')).toEqual(['👋']);
    expect(document.body.querySelector('[data-skin-tones]')).toBeNull();
  });

  it('stars an emote that came without an id by its code', async () => {
    emoteList.push({
      ...emoteList.find((emote) =>
        emote.code === 'catJAM')!,
      code: 'noIdCat',
      id: null,
    });

    await open();
    await openPicker();

    item('noIdCat').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();

    expect(itemsOf('favorites')).toEqual(['noIdCat']);
    emoteList.pop();
  });

  // Stoney 2026-10-14: "right click does nothing anymore, and yes i like that favorite option for sure".
  it('stars an emoji without skin tones on a right-click', async () => {
    await open();
    await openPicker();

    item('😀').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();

    const saved = calls.filter((call) =>
      call.method === 'PUT' && call.path.endsWith('/favorite-emotes'));

    expect(saved.map((call) =>
      call.body)).toEqual([{
      provider: 'Emoji',
      emoteId: '😀',
      code: ':grinning:',
      imageUrl: null,
    }]);

    expect(itemsOf('favorites')).toEqual(['😀']);
  });

  // Stoney 2026-10-14: "someone needs to be able to choose a skin tone once and the picker needs to swap them to
  // that skin tone for all emojis, this is very tedious".
  it('turns every emoji to the one skin tone chosen beside the search', async () => {
    await open();
    await openPicker();

    expect(item('👋').dataset.skins).toBe('5');
    expect(item('👋').textContent.trim()).toBe('👋');

    item('👋').click();
    await settle();
    expect(typed()).toBe('👋 ');

    (picker()!.querySelector('[data-skin-tone-button]') as HTMLElement).click();
    await settle();

    const hands = [...document.body.querySelectorAll<HTMLElement>('[data-tone-choice]')];

    expect(hands.map((hand) =>
      hand.textContent.trim())).toEqual(['✋', '✋🏻', '✋🏼', '✋🏽', '✋🏾', '✋🏿']);

    hands[3]!.click();
    await settle();
    expect(localStorage.getItem('nnz.chat.skin-tone')).toBe('3');
    expect(item('👋').textContent.trim()).toBe('👋🏽');
    expect(item('👏').textContent.trim()).toBe('👏🏽');

    item('👏').click();
    await settle();
    expect(typed()).toBe('👋 👏🏽 ');
  });

  // Stoney 2026-10-14: "make long press clear the choice and the user pick a new version". A long press gives one
  // emoji a tone of its own; the others keep the tone chosen for all.
  it('gives one emoji its own tone on a long press, and asks again on the next one', async () => {
    const tones = (): string[] => [...document.body.querySelectorAll<HTMLElement>('[data-skin-option]')]
      .map((option) =>
        option.dataset.skinOption ?? '');

    localStorage.setItem('nnz.chat.skin-tone', '3');
    await open();
    await openPicker();

    item('👋').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();
    expect(tones()).toEqual(['👋', '👋🏻', '👋🏼', '👋🏽', '👋🏾', '👋🏿']);

    skinOption('👋🏿').click();
    await settle();
    expect(typed()).toBe('👋🏿 ');
    expect(item('👋').textContent.trim()).toBe('👋🏿');
    expect(item('👏').textContent.trim()).toBe('👏🏽');

    item('👋').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await settle();
    expect(tones()).toHaveLength(6);
    expect(JSON.parse(localStorage.getItem('nnz.chat.skin-tones') ?? '{}')).toEqual({});
  });

  it('puts a pick in at the cursor as its own word, stays open, and sends nothing', async () => {
    await open();
    await type('hi there');
    input().editor.commands.setTextSelection(3);
    await openPicker();
    item('catJAM').click();
    await settle();
    expect(typed()).toBe('hi catJAM there');
    expect(picker()).not.toBeNull();

    item('Kappa').click();
    await settle();
    expect(typed()).toBe('hi catJAM Kappa there');
    expect(writes()).toEqual([]);
  });

  it('keeps the recent picks first, newest first, across a reopen', async () => {
    await open();
    await openPicker();
    item('Kappa').click();
    await settle();
    item('bigCat').click();
    await settle();
    expect(sections()[0]).toBe('recent');
    expect(itemsOf('recent')).toEqual(['bigCat', 'Kappa']);
    expect(JSON.parse(localStorage.getItem('nnz.chat.recent-picks')!)).toEqual(['bigCat', 'Kappa']);
  });

  // Stoney 2026-10-14: "instead of removing the tabs, make them grayed out and locked and keep the remaining tabs
  // enabled, but please add the channel grouping".
  it('searches emotes, emoji short codes and emoji tags and keeps them in their sections', async () => {
    await open();
    await openPicker();
    await searchPicker('cat');
    expect(sections()[0]).toBe('channel');
    expect(itemsOf('channel')).toEqual(['catJAM', 'catKISS', 'bigCat']);
    expect(itemsOf('emoji-animals')).toContain('🐱');

    await searchPicker('love');
    expect(itemsOf('emoji-smileys')).toContain('😍');
  });

  it('grays out and locks the rail tabs without a match, and keeps the others', async () => {
    const jump = (id: string): HTMLButtonElement =>
      picker()!.querySelector(`[data-picker-jump="${id}"]`)!;

    await open();
    await openPicker();
    await searchPicker('cat');

    expect(jump('channel').disabled).toBe(false);
    expect(jump('emoji-animals').disabled).toBe(false);
    expect(jump('emoji-flags').disabled).toBe(true);
    expect(jump('channel').querySelector('img')!.getAttribute('src')).toBe('https://cdn.test/bigCat-2.webp');
  });

  // Stoney 2026-10-14: "use the channel avatar instead of its first emote", and the Twitch picker's sections that
  // name their source and fold away.
  it('shows another channel by its avatar and names the source in its heading', async () => {
    emoteList.push({
      ...emoteList[0]!,
      id: 'id-zedHi',
      code: 'zedHi',
      provider: 'Twitch',
      origin: 'other',
      ownerLogin: 'zed',
      ownerName: 'Zed',
      ownerAvatarUrl: 'https://cdn.test/zed.png',
    });

    try {
      await open();
      await openPicker();

      const jump = picker()!.querySelector('[data-picker-jump="owner-zed"]')!;
      const heading = picker()!.querySelector('[data-picker-collapse="owner-zed"]')!;

      expect(jump.querySelector('img')!.getAttribute('src')).toBe('https://cdn.test/zed.png');
      expect(heading.querySelector('img')!.getAttribute('src')).toBe('https://cdn.test/zed.png');
      expect(heading.textContent).toContain('Zed');
      expect(heading.textContent).toContain('Twitch');
    } finally {
      emoteList.pop();
    }
  });

  it('folds a section away from its heading and keeps it folded across a reopen', async () => {
    localStorage.removeItem('nnz.chat.picker-collapsed');
    await open();
    await openPicker();

    const heading = (): HTMLElement => picker()!.querySelector('[data-picker-collapse="channel"]')!;

    heading().click();
    await settle();
    expect(heading().getAttribute('aria-expanded')).toBe('false');
    expect(itemsOf('channel')).toEqual([]);
    expect(JSON.parse(localStorage.getItem('nnz.chat.picker-collapsed')!)).toEqual(['channel']);

    (picker()!.querySelector('[data-picker-jump="channel"]') as HTMLElement).click();
    await settle();
    expect(itemsOf('channel')).toEqual(['bigCat', 'catKISS', 'catJAM', 'Kappa']);
  });

  it('marks a favorite with a star in the grid', async () => {
    favoriteList = [{
      provider: 'Twitch',
      emoteId: 'id-catJAM',
      code: 'catJAM',
      imageUrl: null,
    }];

    await open();
    await openPicker();

    const starred = [...picker()!.querySelectorAll<HTMLElement>('[data-favorite-badge]')].map((badge) =>
      badge.closest<HTMLElement>('[data-picker-item]')!.dataset.pickerItem);

    expect(starred).toEqual(['catJAM', 'catJAM']);
  });

  it('says so when nothing matches', async () => {
    await open();
    await openPicker();
    await searchPicker('zzqqxx');
    expect(picker()!.textContent).toContain('Nothing matches.');
  });

  it('names the item under the pointer and its source below the grid', async () => {
    await open();
    await openPicker();
    item('catKISS').dispatchEvent(new MouseEvent('mouseenter'));
    await settle();
    expect(picker()!.querySelector('[data-picker-preview]')!.textContent).toContain('catKISS');
    expect(picker()!.querySelector('[data-picker-preview]')!.textContent).toContain('Twitch');
  });

  // Stoney 2026-10-14: "maybe only allow both if there is enough space?"
  describe('side panels', () => {
    const realMatchMedia = window.matchMedia;
    let wide = false;
    const listeners: ((event: MediaQueryListEvent) => void)[] = [];

    function screen(isWide: boolean): void {
      wide = isWide;

      window.matchMedia = ((query: string): MediaQueryList => ({
        get matches() {
          return wide && query.includes('min-width: 1800px');
        },
        media: query,
        addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => {
          listeners.push(listener);
        },
        removeEventListener: () => {},
        addListener: () => {},
        removeListener: () => {},
        dispatchEvent: () => false,
        onchange: null,
      }) as MediaQueryList) as typeof window.matchMedia;
    }

    afterEach(() => {
      window.matchMedia = realMatchMedia;
      listeners.length = 0;
    });

    async function openBoth(): Promise<void> {
      await open();
      document.body.querySelector<HTMLElement>('[data-display-toggle]')!.click();
      await settle();
      document.body.querySelector<HTMLElement>('[data-mod-tools-toggle]')!.click();
      await settle();
    }

    const pressed = (selector: string): string | null =>
      document.body.querySelector(selector)!.getAttribute('aria-pressed');

    it('closes the older panel when a second one would squeeze the chat', async () => {
      screen(false);
      await openBoth();

      expect(pressed('[data-display-toggle]')).toBe('false');
      expect(pressed('[data-mod-tools-toggle]')).toBe('true');
    });

    it('keeps two panels open where both fit', async () => {
      screen(true);
      await openBoth();

      expect(pressed('[data-display-toggle]')).toBe('true');
      expect(pressed('[data-mod-tools-toggle]')).toBe('true');
    });

    // Stoney 2026-10-14: "if both were open and i shrink the available size then it does not collapse one".
    it('closes the older panel when the window shrinks with two open', async () => {
      screen(true);
      await openBoth();

      wide = false;

      listeners.forEach((listener) =>
        listener({ matches: false } as MediaQueryListEvent));

      await settle();

      expect(pressed('[data-display-toggle]')).toBe('false');
      expect(pressed('[data-mod-tools-toggle]')).toBe('true');
    });
  });

  // Stoney 2026-10-14: "Remove the input from emojis on mobile", closer to the Twitch app's tray.
  // A narrow window answers the width part only; a phone also has a coarse (touch) pointer.
  function stubScreen(touch: boolean): void {
    window.matchMedia = ((query: string): MediaQueryList => ({
      matches: !query.includes('min-width') && (!query.includes('pointer: coarse') || touch),
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
      onchange: null,
    }) as MediaQueryList) as typeof window.matchMedia;
  }

  // Stoney 2026-10-14 ("geez", popout screenshot): the 526px popout dock got the phone tray with huge emotes.
  describe('in a narrow desktop window', () => {
    const realMatchMedia = window.matchMedia;

    beforeEach(() => stubScreen(false));

    afterEach(() => {
      window.matchMedia = realMatchMedia;
    });

    it('keeps the desktop picker with its search box', async () => {
      await open();
      await openPicker();

      expect(picker()!.querySelector('[data-picker-query]')).not.toBeNull();
    });
  });

  describe('on a phone', () => {
    const realMatchMedia = window.matchMedia;

    beforeEach(() => {
      stubScreen(true);
    });

    afterEach(() => {
      window.matchMedia = realMatchMedia;
    });

    it('opens a tray with no search box and no preview, and leaves the focus off it', async () => {
      await open();
      await openPicker();

      expect(picker()).not.toBeNull();
      expect(picker()!.querySelector('[data-picker-query]')).toBeNull();
      expect(picker()!.querySelector('[data-picker-preview]')).toBeNull();
      expect(picker()!.contains(document.activeElement)).toBe(false);
      expect(itemsOf('channel')).toEqual(['bigCat', 'catKISS', 'catJAM', 'Kappa']);
    });

    // Audit 2026-10-15 (390x844): the skin tone button sat alone on a row under the jump rail, and the grid cut
    // its last row in half. The tone moves into the rail row; the list snaps to whole rows.
    it('puts the skin tone control in the jump rail row and snaps the list to whole rows', async () => {
      await open();
      await openPicker();

      const rail = picker()!.querySelector('[data-picker-rail]')!;
      const tone = picker()!.querySelector('[data-skin-tone-button]');
      const list = picker()!.querySelector('[data-picker-section]')!.parentElement!;
      const rows = [...picker()!.querySelectorAll('[data-picker-item]')];

      expect(rail.contains(tone)).toBe(true);
      expect(rail.contains(rail.querySelector('nav'))).toBe(true);
      expect(picker()!.querySelectorAll('[data-skin-tone-button]')).toHaveLength(1);
      expect(list.classList).toContain('phone:snap-mandatory');
      expect(rows.length).toBeGreaterThan(0);
      expect(rows.every((row) => row.classList.contains('phone:snap-start'))).toBe(true);
    });

    // Coordinator 2026-10-15: fixed cell size, as the Twitch and Discord pickers have. The list is one header plus
    // a whole number of rows tall, so the first screen ends on a whole row.
    it('gives the cells a fixed size and the list a height of whole rows', async () => {
      await open();
      await openPicker();

      const list = picker()!.querySelector('[data-picker-section]')!.parentElement!;
      const grid = picker()!.querySelector('[role="group"]')!;
      const cell = picker()!.querySelector('[data-picker-item]')!;

      expect(list.className).toContain('phone:h-[calc(1.75rem+5*3.125rem-0.125rem)]');
      expect(list.className).toContain('h-[calc(1.75rem+7*2.625rem-0.125rem)]');
      expect(grid.className).toContain('phone:grid-cols-[repeat(auto-fill,3rem)]');
      expect(cell.className).toContain('size-10');
      expect(cell.className).toContain('phone:size-12');
      expect(cell.className).not.toContain('aspect-square');
    });

    // A closed tray that hands the focus back to its button draws a focus ring there, and a focus in the box
    // would pull the keyboard up. The focus goes nowhere.
    it('leaves the focus off the smile button and the box when it closes', async () => {
      await open();
      await openPicker();
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await settle();

      expect(picker()).toBeNull();
      expect(document.activeElement?.closest('[data-emote-picker], [data-composer]') ?? null).toBeNull();
    });
  });

  // Stoney 2026-10-14 sent a phone shot of the smile button with a focus ring after the picker closed. The
  // focus goes back to the box, where the person was typing.
  it('puts the focus back in the box when it closes', async () => {
    await open();
    await openPicker();
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(picker()).toBeNull();
    expect(document.activeElement?.closest('[data-composer]')).not.toBeNull();
  });
});

describe('ChatView dock layout', () => {
  const searchField = (): HTMLElement | null => document.body.querySelector('[data-slot="search-field"]');
  const dockSearchToggle = (): HTMLButtonElement | null =>
    document.body.querySelector('[data-dock-search-toggle]');

  it('drops the page title and the panel header in the popped-out window', async () => {
    await open('/popout/chat');

    expect(document.body.querySelector('h1')).toBeNull();
    expect(document.body.querySelector('[data-slot="panel-card"] h2')).toBeNull();
    expect(document.body.querySelector('[data-dock-bar]')).not.toBeNull();
  });

  it('hides the search field until its toggle is pressed', async () => {
    await open('/popout/chat');

    expect(searchField()).toBeNull();

    dockSearchToggle()!.click();
    await settle();

    expect(searchField()).not.toBeNull();
    expect(dockSearchToggle()!.getAttribute('aria-pressed')).toBe('true');

    dockSearchToggle()!.click();
    await settle();

    expect(searchField()).toBeNull();
  });

  it('keeps the page unchanged outside the popout', async () => {
    await open();

    expect(document.body.querySelector('h1')).not.toBeNull();
    expect(document.body.querySelector('[data-slot="panel-card"] h2')).not.toBeNull();
    expect(searchField()).not.toBeNull();
    expect(dockSearchToggle()).toBeNull();
  });
});
