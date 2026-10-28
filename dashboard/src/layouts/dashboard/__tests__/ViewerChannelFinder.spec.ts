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

import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref, toValue, type MaybeRefOrGetter } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import ViewerChannelFinder from '../ViewerChannelFinder.vue';

const asked: string[] = [];

vi.mock('@/api/channels', () => ({
  useChannelDirectory: (search: MaybeRefOrGetter<string>) => {
    asked.push(toValue(search));

    return {
      data: ref([
        {
          id: 'c1',
          login: 'kitte',
          displayName: 'Kitte',
          profileImageUrl: null,
          isLive: true,
        },
        {
          id: '12826',
          login: 'twitch',
          displayName: 'Twitch',
          profileImageUrl: null,
          isLive: false,
          onBot: false,
        },
      ]),
      isFetching: ref(false),
    };
  },
}));

function buildRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/',
        name: 'home',
        component: { template: '<div />' },
      },
      {
        path: '/me/:channel/chat',
        name: 'viewer-chat',
        component: { template: '<div />' },
      },
      {
        path: '/me/:channel/twitch',
        name: 'viewer-external-chat',
        component: { template: '<div />' },
      },
    ],
  });
}

beforeEach(() => {
  localStorage.clear();
  asked.length = 0;
});

afterEach(() => {
  document.body.innerHTML = '';
});

describe('the viewer channel finder', () => {
  it('lists the directory channels, and picking one opens its viewer chat and remembers it', async () => {
    const router = buildRouter();

    await router.push('/');
    await router.isReady();

    const finder = mount(ViewerChannelFinder, {
      attachTo: document.body,
      global: { plugins: [i18n, router] },
    });

    await finder.get('[data-slot="viewer-channel-finder"]').trigger('click');
    await flushPromises();

    const row = document.body.querySelector<HTMLElement>('[data-slot="directory-channel"]');

    expect(row?.textContent).toContain('Kitte');
    expect(row?.textContent).toContain('Live');

    row?.click();
    await flushPromises();

    expect(router.currentRoute.value.name).toBe('viewer-chat');
    expect(router.currentRoute.value.params.channel).toBe('kitte');

    expect(JSON.parse(localStorage.getItem('nnz.viewerChat.recent') ?? '[]')).toEqual([
      {
        login: 'kitte',
        displayName: 'Kitte',
        profileImageUrl: null,
        id: 'c1',
      },
    ]);

    finder.unmount();
  });

  it('lists a Twitch channel off the bot in its own group, and picking it opens its viewer chat', async () => {
    const router = buildRouter();

    await router.push('/');
    await router.isReady();

    const finder = mount(ViewerChannelFinder, {
      attachTo: document.body,
      global: { plugins: [i18n, router] },
    });

    await finder.get('[data-slot="viewer-channel-finder"]').trigger('click');
    await flushPromises();

    const botRows = document.body.querySelectorAll('[data-slot="directory-channel"]');
    const botNames = Array.from(botRows, (bot) => bot.textContent);
    const row = document.body.querySelector<HTMLElement>('[data-slot="twitch-channel"]');

    expect(botNames.join()).not.toContain('Twitch');
    expect(row?.textContent).toContain('Twitch');
    expect(document.body.textContent).toContain('More on Twitch');

    row?.click();
    await flushPromises();

    expect(router.currentRoute.value.name).toBe('viewer-external-chat');
    expect(router.currentRoute.value.params.channel).toBe('twitch');
    expect(router.currentRoute.value.query.id).toBe('12826');

    finder.unmount();
  });

  // Stoney 2026-10-21 (mobile ledger M-13): searching a channel that was not in the first 20 said "No channel found".
  it('says it is searching, never "No channel found", while the typed search is still being asked', async () => {
    const router = buildRouter();

    await router.push('/');
    await router.isReady();

    const finder = mount(ViewerChannelFinder, {
      attachTo: document.body,
      global: { plugins: [i18n, router] },
    });

    await finder.get('[data-slot="viewer-channel-finder"]').trigger('click');
    await flushPromises();

    const input = document.body.querySelector<HTMLInputElement>('input');

    input!.value = 'Zynkah';
    input!.dispatchEvent(new Event('input', { bubbles: true }));
    await flushPromises();

    expect(document.body.textContent).toContain('Searching');
    expect(document.body.textContent).not.toContain('No channel found');

    await new Promise((done) => setTimeout(done, 300));
    await flushPromises();

    expect(document.body.textContent).toContain('No channel found');
    expect(document.body.textContent).not.toContain('Searching');

    finder.unmount();
  });

  it('shows the channel being read as the button label', async () => {
    const router = buildRouter();

    await router.push('/me/kitte/chat');
    await router.isReady();

    const finder = mount(ViewerChannelFinder, { global: { plugins: [i18n, router] } });

    expect(finder.get('[data-slot="viewer-channel-finder"]').text()).toBe('kitte');
    finder.unmount();
  });
});
