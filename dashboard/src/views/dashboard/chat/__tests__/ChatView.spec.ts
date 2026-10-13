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
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { chatKey, type ChatLine } from '@/api/chat';
import { Toaster } from '@/components/ui/sonner';
import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';
import { useProfileDialogStore } from '@/stores/profileDialog';

import ChatView from '../ChatView.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
  messageId: string;
};
type Answer = { data?: unknown; response: { status: number; ok: boolean } };

const calls: Call[] = [];
let history: ChatLine[] = [];
let sendOk: boolean = true;

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
    GET: async (path: string): Promise<Answer> => {
      calls.push({
        method: 'GET',
        path,
        body: undefined,
        messageId: '',
      });

      if (path.endsWith('/auth/me')) {
        return { data: { data: { username: 'stoney_eagle' } }, response: { status: 200, ok: true } };
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
    DELETE: async (path: string, options?: Options): Promise<Answer> => {
      const messageId = String(options?.params?.path?.messageId);

      calls.push({
        method: 'DELETE',
        path,
        body: undefined,
        messageId,
      });

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

async function open(): Promise<void> {
  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/chat',
        name: 'dashboard-chat',
        component: { template: '<div />' },
      },
      {
        path: '/app/settings',
        name: 'dashboard-settings',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push('/app/chat');
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

const input = (): HTMLInputElement =>
  document.body.querySelector('input[data-composer]') as HTMLInputElement;
const writes = (): Call[] => calls.filter((call) => call.method !== 'GET');

async function type(value: string): Promise<void> {
  input().value = value;
  input().dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function send(): Promise<void> {
  document.body
    .querySelector('form')!
    .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

  await settle();
}

describe('ChatView', () => {
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

  it('shows four tray buttons in order: timeout, delete, ban, profile', async () => {
    await open();
    const names = [...rowOf('m2').querySelectorAll<HTMLElement>('[data-line-action]')].map(
      (button) => button.dataset.lineAction,
    );

    expect(names).toEqual(['timeout', 'delete', 'ban', 'profile']);
  });

  it('keeps every tray button a plain ghost icon with a 20px icon, harm only in the ban confirm', async () => {
    await open();

    for (const name of ['timeout', 'delete', 'ban', 'profile']) {
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

    expect(input().value).toBe('');
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

    expect(input().value).toBe('will not go');
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
        badges: [badge],
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
    expect(rowOf('m1').querySelector('img[data-badge]')).not.toBeNull();
    expect(rowOf('m1').querySelector('img[data-avatar]')).toBeNull();
    expect(rowOf('m1').querySelector('svg[data-platform]')).toBeNull();
    expect(rowOf('m1').className).not.toContain('max-w-[65ch]');
    expect(rowOf('m1').querySelector<HTMLElement>('img[data-emote]')!.style.height).toBe('1.5em');
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

  it('emote size sets the emote height', async () => {
    await open();
    await showPanel();

    const height = (): string =>
      rowOf('m1').querySelector<HTMLElement>('img[data-emote]')!.style.height;

    await pick('emoteSize', display.small);
    expect(height()).toBe('1.2em');
    await pick('emoteSize', display.large);
    expect(height()).toBe('2.3em');
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
      viewer()?.querySelector<HTMLElement>('[data-viewer-close]')?.click();
      await settle();

      expect(viewer()).toBeNull();
      expect(rowOf('m2').dataset.focus).toBeUndefined();
    });

    // Stoney 2026-10-13: the tray covered the end of a long message. It sits above the name, touching the
    // line so the hover holds while the pointer moves up to it.
    it('floats the line actions above the name, never over the message text', async () => {
      await open();
      const tray: HTMLElement = rowOf('m1').querySelector('[data-tray]')!;
      const text: HTMLElement = rowOf('m1').querySelector('p')!;

      expect(tray.parentElement).toBe(text.parentElement);
      expect(tray.classList).toContain('bottom-full');
      expect(tray.classList).toContain('left-0');
      expect(tray.classList).not.toContain('top-1/2');
      // Normal spacing pads the row 8px: the tray lifts 6px past it, keeping a 2px overlap with the row.
      expect(tray.classList).toContain('-translate-y-1.5');
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
