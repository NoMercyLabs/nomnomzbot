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

import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import MediaShareView from '../MediaShareView.vue';

type Options = {
  params?: { path?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  path: string;
  body: Record<string, unknown> | undefined;
};
type Row = Record<string, unknown>;

const calls: Call[] = [];
let queue: Row[] = [];
let overlayAttached = true;
// What the bot's player read returns; null answers a 404 like a bot without the read.
let playerState: Record<string, unknown> | null = null;

const config = {
  isEnabled: true,
  requireApproval: true,
  allowTwitchClips: true,
  allowYouTube: false,
  maxDurationSeconds: 60,
  entryCost: 0,
  maxQueueLength: 10,
  perUserCooldownSeconds: 30,
};

const item = (id: string, status: string) =>
  ({
    id,
    requesterDisplayName: `viewer-${id}`,
    sourceType: 'twitch_clip',
    sourceUrl: `https://clips.example/${id}`,
    title: `Clip ${id}`,
    status,
    durationSeconds: 42,
    requestedAt: '2026-10-01T10:00:00Z',
  }) as const satisfies Row;

const answer = (data: unknown): { data: { data: unknown }; response: { status: number } } => ({
  data: { data },
  response: { status: 200 },
});

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      if (path.endsWith('/overlays/required'))
        return answer([{ key: 'media_share', widget: { isAttached: overlayAttached } }]);

      if (path.endsWith('/player')) {
        return playerState === null
          ? { error: {}, response: { status: 404 } }
          : answer(playerState);
      }

      return path.endsWith('/config')
        ? answer(config)
        : answer(queue);
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        body: options?.body,
      });

      return { response: { status: 200 } };
    },
    PUT: async (path: string, options?: Options) => {
      calls.push({
        method: 'PUT',
        path,
        body: options?.body,
      });

      return { response: { status: 200 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  overlayAttached = true;
  playerState = null;

  // The volume slider measures itself; jsdom has no ResizeObserver.
  vi.stubGlobal('ResizeObserver', class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  });

  queue = [
    item('p', 'playing'),
    item('a', 'pending'),
    item('b', 'approved'),
    item('c', 'approved'),
    item('h', 'played'),
  ];

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  // The frame mounts the one TooltipProvider; the page under test needs one above it.
  const framed = defineComponent(() => () => h(TooltipProvider, () =>
    h(MediaShareView)));

  mounted = mount(framed, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

// The row attributes sit on the body of the SummaryRow; the action buttons are beside it, so the row is its parent.
const rowOf = (id: string): HTMLElement => {
  const bodies = [...document.body.querySelectorAll<HTMLElement>('[data-media-share-row]')];
  const body = bodies.find((row) => row.textContent?.includes(`Clip ${id}`));

  return body?.parentElement as HTMLElement;
};

const press = async (root: HTMLElement | Document, selector: string): Promise<void> => {
  root.querySelector<HTMLButtonElement>(selector)?.click();
  await flushPromises();
};

const openMenu = async (row: HTMLElement): Promise<void> => {
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;
  Element.prototype.scrollIntoView = (): void => undefined;

  row.querySelector<HTMLElement>('[data-action="more"]')!.dispatchEvent(
    new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
  );

  await settle();
};

const menuItem = (action: string): HTMLElement | null =>
  document.body.querySelector<HTMLElement>(`[role="menuitem"][data-action="${action}"]`);

const openSettings = async (): Promise<void> => {
  await press(document, '[data-action="open-settings"]');
  await settle();
};

describe('Media share page', () => {
  it('approves a pending item with the approve endpoint', async () => {
    await open();
    await press(rowOf('a'), '[data-action="approve"]');

    expect(calls).toEqual([
      {
        method: 'POST',
        path: '/api/v{version}/media-share/{id}/approve',
        body: undefined,
      },
    ]);
  });

  it('rejects a pending item and skips an approved one', async () => {
    await open();
    await press(rowOf('a'), '[data-action="reject"]');
    await press(rowOf('b'), '[data-action="skip"]');

    expect(calls.map((call) => call.path)).toEqual([
      '/api/v{version}/media-share/{id}/reject',
      '/api/v{version}/media-share/{id}/skip',
    ]);
  });

  it('plays the second approved item now: first in line, then a skip ends what plays', async () => {
    await open();
    await press(rowOf('c'), '[data-action="play-now"]');
    await settle();

    expect(calls).toEqual([
      {
        method: 'POST',
        path: '/api/v{version}/media-share/{id}/reorder',
        body: { position: 1 },
      },
      {
        method: 'POST',
        path: '/api/v{version}/channels/{channelId}/actions/run',
        body: { actionType: 'media_playback', parameters: { command: 'skip', value: '' } },
      },
    ]);
  });

  it('shows the playing item in the player card, and Pause, Skip and Stop drive the overlay', async () => {
    await open();

    const player = document.body.querySelector<HTMLElement>('[data-media-share-player]')!;

    expect(player.querySelector('[data-player-title]')?.textContent).toContain('Clip p');
    expect(player.querySelector('[data-action="toggle-play"]')?.textContent).toContain('Pause');

    await press(player, '[data-action="toggle-play"]');
    await press(player, '[data-action="player-skip"]');
    await press(player, '[data-action="player-stop"]');

    expect(calls.map((call) => call.body?.parameters)).toEqual([
      { command: 'pause', value: '' },
      { command: 'skip', value: '' },
      { command: 'stop', value: '' },
    ]);
  });

  it('reads the player on load: time, length and the stream volume', async () => {
    playerState = {
      state: 'playing',
      requestId: 'p',
      title: 'Clip p',
      sharerName: 'viewer',
      thumbnailUrl: null,
      positionSeconds: 65,
      durationSeconds: 200,
      volume: 40,
    };

    await open();

    const player = document.body.querySelector<HTMLElement>('[data-media-share-player]')!;

    expect(player.querySelector('[data-player-time]')?.textContent?.replace(/\s+/g, ' ').trim())
      .toBe('1:05 / 3:20');

    expect(player.querySelector('[data-player-volume] [role="slider"]')?.getAttribute('aria-valuenow'))
      .toBe('40');

    expect(player.querySelector('[data-player-seek] [role="slider"]')?.getAttribute('aria-valuemax'))
      .toBe('200');
  });

  it('seeks the overlay to the place the streamer picks', async () => {
    playerState = {
      state: 'playing',
      requestId: 'p',
      title: 'Clip p',
      sharerName: 'viewer',
      thumbnailUrl: null,
      positionSeconds: 10,
      durationSeconds: 200,
      volume: 40,
    };

    await open();

    const thumb = document.body.querySelector<HTMLElement>('[data-player-seek] [role="slider"]')!;

    thumb.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    await settle();

    expect(calls.map((call) => call.body?.parameters)).toEqual([{ command: 'seek', value: '200' }]);
  });

  it('starts an idle player with Play when items are approved', async () => {
    queue = [item('b', 'approved')];
    await open();

    const player = document.body.querySelector<HTMLElement>('[data-media-share-player]')!;

    expect(player.querySelector('[data-player-title]')).toBeNull();
    expect(player.querySelector('[data-action="player-skip"]')).toBeNull();

    await press(player, '[data-action="toggle-play"]');

    expect(calls).toEqual([
      {
        method: 'POST',
        path: '/api/v{version}/channels/{channelId}/actions/run',
        body: { actionType: 'media_playback', parameters: { command: 'play', value: '' } },
      },
    ]);
  });

  it('says nothing plays on stream while the media share source is not loaded in OBS', async () => {
    overlayAttached = false;
    await open();

    expect(document.body.querySelector('[data-player-overlay-missing]')?.textContent)
      .toContain('not loaded in OBS');
  });

  it('shows no overlay warning once the media share source is loaded', async () => {
    await open();

    expect(document.body.querySelector('[data-player-overlay-missing]')).toBeNull();
  });

  it('disables Play and says why when nothing is approved', async () => {
    queue = [item('a', 'pending')];
    await open();

    const player = document.body.querySelector<HTMLElement>('[data-media-share-player]')!;

    expect(player.querySelector<HTMLButtonElement>('[data-action="toggle-play"]')?.disabled).toBe(true);
    expect(player.querySelector('[data-player-reason]')?.textContent).toContain('Approve an item');
  });

  it('offers no play now on the first approved item and no approve on an approved one', async () => {
    await open();

    expect(rowOf('b').querySelector('[data-action="play-now"]')).toBeNull();
    expect(rowOf('b').querySelector('[data-action="approve"]')).toBeNull();
  });

  it('removes an approved item only after the confirm, with the reject endpoint', async () => {
    await open();
    await press(rowOf('c'), '[data-action="remove"]');

    expect(calls).toEqual([]);

    await press(document, '[data-confirm-action]');

    expect(calls.map((call) => call.path)).toEqual(['/api/v{version}/media-share/{id}/reject']);
  });

  it('moves an approved item down from the more menu', async () => {
    await open();
    await openMenu(rowOf('b'));

    expect(menuItem('up')).not.toBeNull();
    expect(menuItem('up')!.hasAttribute('data-disabled')).toBe(true);

    menuItem('down')!.click();
    await settle();

    expect(calls).toEqual([
      {
        method: 'POST',
        path: '/api/v{version}/media-share/{id}/reorder',
        body: { position: 2 },
      },
    ]);
  });

  it('groups the queue into waiting, up next and history, and history has no controls', async () => {
    await open();

    const sections = [...document.body.querySelectorAll<HTMLElement>('[data-queue-section]')];

    expect(rowOf('p')).toBeUndefined();

    expect(sections.map((section) => section.dataset.queueSection)).toEqual([
      'pending',
      'approved',
      'history',
    ]);

    expect(rowOf('h').querySelectorAll('button')).toHaveLength(0);
  });

  it('keeps the settings out of the page until the Settings button opens them', async () => {
    await open();

    expect(document.body.querySelector('[data-media-share-settings]')).toBeNull();

    await openSettings();

    expect(document.body.querySelector('[data-media-share-settings]')).not.toBeNull();
  });

  it('saves the whole config with the changed value', async () => {
    await open();
    await openSettings();

    const field = document.body.querySelector<HTMLInputElement>('[data-field="maxQueueLength"]');

    field!.value = '25';
    field!.dispatchEvent(new Event('input'));
    await flushPromises();
    await press(document, '[data-action="save"]');

    expect(calls).toEqual([
      {
        method: 'PUT',
        path: '/api/v{version}/media-share/config',
        body: { ...config, maxQueueLength: 25 },
      },
    ]);
  });

  it('saves subscribers only as the one eligibility rule the bot enforces', async () => {
    await open();
    await openSettings();

    const rule = document.body.querySelector<HTMLElement>('[data-field="subOnly"] button');

    expect(rule).not.toBeNull();

    rule!.click();
    await flushPromises();
    await press(document, '[data-action="save"]');

    expect(calls).toEqual([
      {
        method: 'PUT',
        path: '/api/v{version}/media-share/config',
        body: { ...config, eligibilityJson: '{"subOnly":true}' },
      },
    ]);
  });
});
