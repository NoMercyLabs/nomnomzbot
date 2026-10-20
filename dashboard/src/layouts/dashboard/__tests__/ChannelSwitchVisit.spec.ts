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
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import ChannelSwitch from '../ChannelSwitch.vue';

vi.mock('@/api/channels', () => ({
  useChannelDirectory: () => ({
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
  }),
}));

vi.mock('@/api/viewAs', () => ({ useViewAs: () => ({ role: ref('') }) }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channels: ref([
      {
        kind: 'own',
        login: 'stoney',
        displayName: 'Stoney',
        profileImageUrl: null,
        isLive: false,
        channelId: 'own1',
      },
    ]),
    current: ref({
      kind: 'own',
      login: 'stoney',
      displayName: 'Stoney',
      profileImageUrl: null,
      isLive: false,
      channelId: 'own1',
    }),
    isPending: ref(false),
    choose: vi.fn(),
  }),
}));

function buildRouter() {
  const page = { template: '<div />' };

  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/u/:channel/commands',
        name: 'commands',
        component: page,
      },
      {
        path: '/me/:channel',
        name: 'viewer-me',
        component: page,
      },
      {
        path: '/me/:channel/twitch',
        name: 'viewer-external-chat',
        component: page,
      },
    ],
  });
}

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  document.body.innerHTML = '';
});

describe('the channel switch Visit a channel group on a page that is not chat', () => {
  async function openSwitch() {
    const router = buildRouter();

    await router.push('/app/u/stoney/commands');
    await router.isReady();

    const wrapper = mount(ChannelSwitch, {
      attachTo: document.body,
      global: { plugins: [i18n, router] },
    });

    await wrapper.get('[data-slot="channel-switch"]').trigger('click');
    await flushPromises();

    return { router, wrapper };
  }

  it('lists the directory channels under their own heading beside the own channel', async () => {
    const { wrapper } = await openSwitch();

    expect(document.body.textContent).toContain('Visit a channel');

    expect(document.body.querySelector('[data-slot="directory-channel"]')?.textContent).toContain(
      'Kitte',
    );

    wrapper.unmount();
  });

  it('opens an on-bot channel at its viewer home /me/<login>', async () => {
    const { router, wrapper } = await openSwitch();

    document.body.querySelector<HTMLElement>('[data-slot="directory-channel"]')?.click();
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe('/me/kitte');

    wrapper.unmount();
  });

  it('opens an off-bot channel at its Twitch chat /me/<login>/twitch?id=<twitch id>', async () => {
    const { router, wrapper } = await openSwitch();

    document.body.querySelector<HTMLElement>('[data-slot="twitch-channel"]')?.click();
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe('/me/twitch/twitch?id=12826');

    wrapper.unmount();
  });
});
