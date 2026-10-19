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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { activeGameKey, gameConfigsKey } from '@/api/games';
import { i18n } from '@/i18n';

import GamesView from '../GamesView.vue';
import en from '../i18n/en.json';

const catalog = [
  {
    gameKey: 'trivia',
    displayName: 'Trivia',
    inputKeywords: ['a', 'b'],
    overlayWidgetKey: 'trivia',
    minPlayers: 2,
    maxPlayers: 20,
    lobbyWindowSeconds: 30,
    requiresEntryFee: false,
  },
];

const config = {
  id: 'g1',
  gameType: 'coinflip',
  category: 'gambling',
  isEnabled: false,
  requires18Plus: true,
  minBet: 10,
  cooldownSeconds: 600,
  cooldownFloorSeconds: 300,
  permission: 'Everyone',
};

const state = vi.hoisted(() => ({
  active: null as null | Record<string, unknown>,
  calls: [] as {
    method: string;
    path: string;
    body?: unknown;
  }[],
}));

const ok = (data: unknown) => ({ data: { data }, response: { status: 200, ok: true } });

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      if (path.endsWith('/sessions/catalog'))
        return ok(catalog);

      if (path.endsWith('/sessions/active'))
        return ok(state.active);

      if (path.endsWith('/economy/games'))
        return ok([config]);

      return ok([]);
    },
    POST: async (path: string, init: { body: unknown }) => {
      state.calls.push({
        method: 'POST',
        path,
        body: init.body,
      });

      return { response: { status: 200, ok: true } };
    },
    DELETE: async (path: string) => {
      state.calls.push({ method: 'DELETE', path });

      return { response: { status: 200, ok: true } };
    },
    PUT: async (path: string, init: { body: unknown }) => {
      state.calls.push({
        method: 'PUT',
        path,
        body: init.body,
      });

      return { response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

const allKeys = [
  'games:session:read',
  'games:session:start',
  'games:session:cancel',
  'economy:games:read',
  'economy:games:write',
];
const access = vi.hoisted(() => ({ held: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => access.held.includes(key),
  }),
}));

let mounted: VueWrapper | null = null;
let queryClient = new QueryClient();

beforeEach(() => {
  access.held = allKeys;
  localStorage.clear();
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
  state.active = null;
  state.calls = [];
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(GamesView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 200));
  await flushPromises();
}

const button = (selector: string): HTMLElement => document.querySelector(selector) as HTMLElement;

const runningSession = {
  id: 's1',
  gameType: 'trivia',
  status: 'Lobby',
  participantCount: 3,
  startedAt: '2026-10-17T10:00:00Z',
};

describe('GamesView gating', () => {
  it('shows no panel and no action to a person who holds no game key', async () => {
    access.held = [];
    state.active = runningSession;

    await open();

    expect(document.body.textContent).not.toContain(en.dashboard.games.active.title);
    expect(document.body.textContent).not.toContain(en.dashboard.games.catalog.title);
    expect(document.body.textContent).not.toContain(en.dashboard.games.configs.title);
    expect(document.body.textContent).not.toContain(en.dashboard.games.history.title);
    expect(document.querySelector('[data-start-game]')).toBeNull();
    expect(document.querySelector('[data-cancel-game]')).toBeNull();
    expect(document.querySelector('[data-edit-game]')).toBeNull();
  });

  it('shows the read-only panels without start, cancel or edit when only the read keys are held', async () => {
    access.held = ['games:session:read', 'economy:games:read'];
    state.active = runningSession;

    await open();

    expect(document.body.textContent).toContain(en.dashboard.games.catalog.title);
    expect(document.body.textContent).toContain(en.dashboard.games.configs.title);
    expect(document.querySelector('[data-start-game]')).toBeNull();
    expect(document.querySelector('[data-cancel-game]')).toBeNull();
    expect(document.querySelector('[data-edit-game]')).toBeNull();

    const row = button('[data-game-config="coinflip"]');
    const shown = row.querySelector('[data-slot="enabled-state"]');

    expect(row.querySelector('[role="switch"]')).toBeNull();
    expect(shown?.getAttribute('data-state')).toBe('off');
    expect(shown?.textContent?.trim()).toBe('Off');
  });

  it('shows start, cancel and edit when their keys are held', async () => {
    state.active = runningSession;

    await open();

    expect(document.querySelector('[data-start-game="trivia"]')).not.toBeNull();
    expect(document.querySelector('[data-cancel-game]')).not.toBeNull();
    expect(document.querySelector('[data-edit-game="coinflip"]')).not.toBeNull();
  });
});

describe('GamesView', () => {
  it('starts the chosen game with its key', async () => {
    await open();

    expect(document.body.textContent).toContain('Trivia');

    button('[data-start-game="trivia"]').click();
    await flushPromises();

    expect(state.calls).toEqual([
      {
        method: 'POST',
        path: '/api/v{version}/channels/{channelId}/games/sessions',
        body: { gameType: 'trivia' },
      },
    ]);
  });

  it('cancels the running game only after the confirm', async () => {
    state.active = {
      id: 's1',
      gameType: 'trivia',
      status: 'Lobby',
      participantCount: 3,
      startedAt: '2026-10-17T10:00:00Z',
    };

    await open();

    button('[data-cancel-game]').click();
    await flushPromises();

    expect(state.calls).toEqual([]);

    const confirm = Array.from(document.querySelectorAll('[role="alertdialog"] button')).find(
      (candidate) => candidate.textContent?.trim() === en.dashboard.games.active.cancelConfirm,
    ) as HTMLElement | undefined;

    confirm?.click();
    await flushPromises();

    expect(state.calls.map((call) => `${call.method} ${call.path}`)).toEqual([
      'DELETE /api/v{version}/channels/{channelId}/games/sessions/{sessionId}',
    ]);
  });

  it('turns a mini-game on by sending the whole setup with the new flag', async () => {
    await open();

    button('[data-game-config="coinflip"] [role="switch"]').click();
    await flushPromises();

    const call = state.calls[0];

    expect(call?.method).toBe('PUT');

    expect(call?.body).toMatchObject({
      gameType: 'coinflip',
      isEnabled: true,
      requires18Plus: true,
      minBet: 10,
    });

    expect(call?.body).not.toHaveProperty('id');
  });

  it('saves the edited limits as numbers and sends null for an emptied field', async () => {
    await open();

    button('[data-edit-game="coinflip"]').click();
    await flushPromises();

    const field = (name: string): HTMLInputElement =>
      document.querySelector(`[data-game-field="${name}"]`) as HTMLInputElement;

    expect(field('minBet').value).toBe('10');
    expect(field('maxBet').value).toBe('');

    field('minBet').value = '25';
    field('minBet').dispatchEvent(new Event('input'));
    field('maxBet').value = '500';
    field('maxBet').dispatchEvent(new Event('input'));
    field('minBet').value = '';
    field('minBet').dispatchEvent(new Event('input'));
    field('winChancePercent').value = '45.5';
    field('winChancePercent').dispatchEvent(new Event('input'));
    await flushPromises();

    button('[data-save-game-config]').click();
    await flushPromises();

    const call = state.calls[0];

    expect(call?.method).toBe('PUT');

    expect(call?.body).toMatchObject({
      gameType: 'coinflip',
      isEnabled: false,
      cooldownSeconds: 600,
      permission: 'Everyone',
      minBet: null,
      maxBet: 500,
      winChancePercent: 45.5,
      houseEdgePercent: null,
      payoutMultiplier: null,
      maxPlaysPerStream: null,
    });

    expect(call?.body).not.toHaveProperty('id');
  });

  it('edits the cooldown and the permission, and shows the floor the server enforces', async () => {
    await open();

    button('[data-edit-game="coinflip"]').click();
    await flushPromises();

    const cooldown = document.querySelector(
      '[data-game-field="cooldownSeconds"]',
    ) as HTMLInputElement;

    expect(cooldown.value).toBe('600');
    expect(document.querySelector('[data-game-cooldown-floor]')?.textContent).toContain('300');

    cooldown.value = '900';
    cooldown.dispatchEvent(new Event('input'));
    await flushPromises();

    // jsdom lacks these three; the select calls them when it opens.
    Element.prototype.hasPointerCapture = (): boolean => false;
    Element.prototype.releasePointerCapture = (): void => undefined;
    Element.prototype.scrollIntoView = (): void => undefined;

    document.querySelector('[data-game-permission]')!.dispatchEvent(
      new PointerEvent('pointerdown', {
        bubbles: true,
        button: 0,
        pointerType: 'mouse',
      }),
    );

    await flushPromises();

    const option = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find((el) =>
      el.textContent?.includes(en.dashboard.games.configs.standings.Vip),
    )!;

    option.dispatchEvent(
      new PointerEvent('pointerup', {
        bubbles: true,
        button: 0,
        pointerType: 'mouse',
      }),
    );

    await flushPromises();

    button('[data-save-game-config]').click();
    await flushPromises();

    expect(state.calls[0]?.body).toMatchObject({
      cooldownSeconds: 900,
      permission: 'Vip',
    });

    expect(state.calls[0]?.body).not.toHaveProperty('cooldownFloorSeconds');
  });

  it('marks the game queries stale on a ConfigChanged with an unlisted domain', async () => {
    await open();

    const { channelPrefixes } = await import('@/realtime/configDomains');
    const keys = [activeGameKey('c1'), gameConfigsKey('c1')];

    expect(
      keys.every((key) =>
        channelPrefixes.some((prefix) => prefix('c1').every((part, i) => key[i] === part)),
      ),
    ).toBe(true);
  });
});
