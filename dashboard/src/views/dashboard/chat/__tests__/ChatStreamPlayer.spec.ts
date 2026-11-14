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
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';
import { servedOrigin } from '@/lib/botAddress';

import ChatView from '../ChatView.vue';
import en from '../i18n/en.json';

// Stoney 2026-11-13: "can you embed the twitch player in the chat view, and make it look and feel like the twitch app?
// Player should be a toggle", then "I mean mine, The one we use in the overlay source."
// The player package is the network edge (it talks to Twitch); everything else is the real page.
type PluginCall = { name: string; options: unknown };
type FakePlayer = {
  containerId: string;
  plugins: PluginCall[];
  setup: Record<string, unknown> | null;
  disposed: boolean;
};

const { players } = vi.hoisted(() =>
  ({ players: [] as FakePlayer[] }));

vi.mock('@nomercy-entertainment/nomercy-video-player', () => ({
  default: (containerId: string) => {
    const player: FakePlayer = {
      containerId,
      plugins: [],
      setup: null,
      disposed: false,
    };

    players.push(player);

    const api = {
      addPlugin: (plugin: { name: string }, options?: unknown) => {
        player.plugins.push({ name: plugin.name, options });

        return api;
      },
      setup: (options: Record<string, unknown>) => {
        player.setup = options;

        return api;
      },
      on: () =>
        api,
      dispose: async () => {
        player.disposed = true;
      },
    };

    return api;
  },
}));

vi.mock('@nomercy-entertainment/nomercy-video-player/plugins/twitch', () => ({
  TwitchPlugin: class TwitchPlugin {},
}));

vi.mock('@nomercy-entertainment/nomercy-video-player/plugins/desktop-ui', () => ({
  DesktopUiPlugin: class DesktopUiPlugin {},
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () =>
      undefined,
    GET: async (path: string) => {
      if (path.endsWith('/roles/effective/me'))
        return { data: { data: { heldActionKeys: ['moderation:chat:settings:read', 'chat:read', 'chat:send'] } }, response: { status: 200, ok: true } };

      if (path.endsWith('/auth/me'))
        return { data: { data: { username: 'stoney_eagle' } }, response: { status: 200, ok: true } };

      return { data: { data: [] }, response: { status: 200, ok: true } };
    },
  },
}));

// The channel the page shows; a test turns it offline or live through this one shared ref.
const channel = vi.hoisted(() => ({ current: null as { value: { isLive: boolean } } | null }));

vi.mock('@/composables/useCurrentChannel', async () => {
  const vue = await import('vue');
  const current = vue.ref({
    login: 'stoney_eagle',
    displayName: 'Stoney_Eagle',
    isLive: true,
  });

  channel.current = current;

  return {
    useCurrentChannel: () => ({
      channelId: vue.ref('c1'),
      current,
    }),
  };
});

let mounted: VueWrapper | null = null;

beforeEach(() => {
  players.length = 0;
  localStorage.clear();

  if (channel.current) {
    channel.current.value.isLive = true;
  }

  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal('ResizeObserver', class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

const settle = async (): Promise<void> => {
  await flushPromises();

  await new Promise((done) =>
    setTimeout(done, 0));

  await flushPromises();
};

async function open(path = '/app/chat'): Promise<void> {
  const router = createRouter({
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
    ],
  });

  await router.push(path);

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const framed = defineComponent(() =>
    () =>
      h(TooltipProvider, () =>
        h(ChatView)));

  mounted = mount(framed, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), router, [VueQueryPlugin, { queryClient }]] },
  });

  await settle();
}

const toggle = (): HTMLButtonElement | null =>
  document.body.querySelector<HTMLButtonElement>('[data-player-toggle]');

// The player is its own chunk, loaded the first time the stream is turned on, so a build is awaited.
async function playerBuilt(): Promise<void> {
  await vi.waitFor(() =>
    expect(players.length).toBeGreaterThan(0));
}

async function flip(): Promise<void> {
  toggle()!.click();
  await settle();
}

async function turnOn(): Promise<void> {
  await flip();
  await playerBuilt();
}

describe('ChatView stream player', () => {
  it('is off by default: the toggle is not pressed and no player is built', async () => {
    await open();

    expect(toggle()).not.toBeNull();
    expect(toggle()!.getAttribute('aria-pressed')).toBe('false');
    expect(players).toHaveLength(0);
    expect(document.body.querySelector('[data-stream-player]')).toBeNull();
  });

  it('turned on, builds the NoMercy player through the live playlist proxy, on the channel, muted', async () => {
    await open();
    await turnOn();

    expect(toggle()!.getAttribute('aria-pressed')).toBe('true');
    expect(players).toHaveLength(1);

    const player = players[0]!;

    expect(player.plugins.map((plugin) =>
      plugin.name)).toEqual(['TwitchPlugin', 'DesktopUiPlugin']);

    expect(player.plugins[0]!.options).toEqual({ proxyUrl: `${servedOrigin}/api/v1/stream/playlist` });

    expect(player.setup).toMatchObject({
      muted: true,
      autoPlay: true,
      playlist: [expect.objectContaining({ url: 'https://www.twitch.tv/stoney_eagle' })],
    });

    expect(document.getElementById(player.containerId)).not.toBeNull();
  });

  it('remembers the choice: a remounted page builds the player again with no click', async () => {
    await open();
    await turnOn();
    mounted!.unmount();
    mounted = null;
    players.length = 0;

    await open();
    await playerBuilt();

    expect(toggle()!.getAttribute('aria-pressed')).toBe('true');
    expect(players).toHaveLength(1);
  });

  it('turned off, disposes the player and removes it from the page', async () => {
    await open();
    await turnOn();
    await flip();

    expect(players[0]!.disposed).toBe(true);
    expect(document.body.querySelector('[data-stream-player]')).toBeNull();
    expect(toggle()!.getAttribute('aria-pressed')).toBe('false');
  });

  it('disposes the player when the page leaves', async () => {
    await open();
    await turnOn();
    mounted!.unmount();
    mounted = null;

    expect(players[0]!.disposed).toBe(true);
  });

  // Stoney 2026-11-13: "make it look and feel like the twitch app". A phone gets the toggle in its slim header and the
  // stream above the chat, as in the Twitch app.
  it('on a phone, the header toggle shows the stream above the chat', async () => {
    const realMatchMedia = window.matchMedia;

    window.matchMedia = ((query: string): MediaQueryList => ({
      matches: !query.includes('min-width'),
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
      onchange: null,
    }) as MediaQueryList) as typeof window.matchMedia;

    try {
      await open();

      const dockToggle = document.body.querySelector<HTMLButtonElement>('[data-dock-player-toggle]');

      expect(dockToggle).not.toBeNull();
      expect(dockToggle!.getAttribute('aria-pressed')).toBe('false');

      dockToggle!.click();
      await settle();
      await playerBuilt();

      expect(players[0]!.setup).toMatchObject({
        muted: true,
        playlist: [expect.objectContaining({ url: 'https://www.twitch.tv/stoney_eagle' })],
      });

      const stream = document.body.querySelector('[data-stream-player]');
      const feed = document.body.querySelector('[data-page-body] [data-slot="panel-card"]');

      expect(stream).not.toBeNull();
      expect(feed).not.toBeNull();
      const order = stream!.compareDocumentPosition(feed!);

      expect(order & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    } finally {
      window.matchMedia = realMatchMedia;
    }
  });

  // Stoney 2026-11-13, on the offline channel's "Something went wrong" (screenshot): "it should show the stream as
  // offline and that button does not match".
  it('shows an offline channel as offline and builds no player for it', async () => {
    channel.current!.value.isLive = false;

    await open();
    await flip();

    expect(players).toHaveLength(0);
    expect(document.body.querySelector('[data-stream-offline]')?.textContent?.trim()).toBe('Stoney_Eagle is offline');
  });

  it('builds the player once the offline channel goes live', async () => {
    channel.current!.value.isLive = false;

    await open();
    await flip();
    channel.current!.value.isLive = true;
    await settle();
    await playerBuilt();

    expect(players).toHaveLength(1);
    expect(document.body.querySelector('[data-stream-offline]')).toBeNull();
  });

  it('draws the stream toggle like its neighbors: an outline button with its name beside the icon', async () => {
    await open();

    const settings = document.body.querySelector('[data-mod-tools-toggle]');

    expect(toggle()!.getAttribute('data-variant')).toBe('outline');
    expect(toggle()!.getAttribute('data-variant')).toBe(settings!.getAttribute('data-variant'));
    expect(toggle()!.getAttribute('data-size')).toBe(settings!.getAttribute('data-size'));
    expect(toggle()!.textContent?.trim()).toBe('Stream');
  });

  // Stoney 2026-11-14: "the chat is also not wide enough when showing the player" (340 px wrapped most lines).
  it('keeps the chat column beside the player wide enough for a line with its badges', async () => {
    await open();
    await turnOn();

    const panel = document.body.querySelector('[data-page-body] [data-slot="panel-card"]')!;

    expect(panel.classList.contains('lg:w-96')).toBe(true);
    expect(panel.classList.contains('2xl:w-md')).toBe(true);
  });

  it('gives the docked chat no toggle and no player, even when the choice is on', async () => {
    localStorage.setItem('nnz.chat.player', 'true');

    await open('/popout/chat');

    expect(toggle()).toBeNull();
    expect(players).toHaveLength(0);
  });
});
