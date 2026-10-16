// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
      },
    ]);

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
