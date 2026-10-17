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
  cooldownSeconds: 5,
  permission: 'everyone',
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

let mounted: VueWrapper | null = null;
let queryClient = new QueryClient();

beforeEach(() => {
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

const button = (selector: string): HTMLElement =>
  document.querySelector(selector) as HTMLElement;

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

  it('marks the game queries stale on a ConfigChanged with an unlisted domain', async () => {
    await open();

    const { channelPrefixes } = await import('@/realtime/configDomains');
    const keys = [activeGameKey('c1'), gameConfigsKey('c1')];

    expect(
      keys.every((key) => channelPrefixes.some((prefix) => prefix('c1').every((part, i) => key[i] === part))),
    ).toBe(true);
  });
});
