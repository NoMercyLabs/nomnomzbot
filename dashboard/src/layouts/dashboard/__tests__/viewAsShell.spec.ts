// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, RouterView, type Router } from 'vue-router';

import { queryClient } from '@/api/queryClient';
import { viewAs } from '@/api/viewAs';
import { i18n } from '@/i18n';

import ViewerLayout from '../../ViewerLayout.vue';
import ProfileMenu from '../ProfileMenu.vue';

vi.mock('@/api/auth', () => ({
  useCurrentUser: () => ({
    data: ref({
      displayName: 'Stoney_Eagle',
      profileImageUrl: null,
      isAdmin: false,
    }),
  }),
  signOut: vi.fn<() => Promise<void>>(async () => undefined),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    current: ref({ kind: 'own', login: 'stoney_eagle' }),
  }),
}));

const page = defineComponent({ render: () => h('div', { 'data-slot': 'page' }) });
const root = defineComponent({ render: () => h('div', [h(ProfileMenu), h(RouterView)]) });

// The viewer frame without the parts that read the network: its banner is what this spec looks at.
const viewerFrame = defineComponent({
  components: { ViewerLayout },
  template: '<ViewerLayout />',
});

let wrapper: VueWrapper | null = null;
let router: Router;

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        name: 'dashboard-commands',
        path: '/app/u/:channelName/commands',
        component: page,
      },
      {
        path: '/me',
        component: viewerFrame,
        children: [{
          name: 'viewer-me',
          path: ':channel',
          component: page,
        }],
      },
    ],
  });
}

async function openPreviewFromCommands(): Promise<void> {
  router = makeRouter();
  await router.push('/app/u/stoney_eagle/commands');

  wrapper = mount(root, {
    attachTo: document.body,
    global: {
      plugins: [i18n, router],
      stubs: {
        ViewerNav: true,
        ViewerChannelFinder: true,
        LanguagePicker: true,
        Toaster: true,
      },
    },
  });

  await wrapper.get('[data-slot="profile-menu"]').trigger('click');
  await flushPromises();

  const entry = [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find((node) =>
    node.textContent?.includes('View as viewer'),
  );

  entry!.click();
  await flushPromises();
}

beforeEach(() => {
  viewAs.value = '';
});

afterEach(() => {
  wrapper?.unmount();
  wrapper = null;
  document.body.innerHTML = '';
  viewAs.value = '';
  localStorage.clear();
});

describe('View as viewer opens the real viewer shell', () => {
  it('moves from the operator page to the viewer shell of the own channel and shows the banner', async () => {
    await openPreviewFromCommands();

    expect(router.currentRoute.value.fullPath).toBe('/me/stoney_eagle');
    expect(viewAs.value).toBe('viewer');

    const banner = document.body.querySelector('[data-slot="view-as-banner"]');

    expect(banner?.textContent).toContain('Viewing as a viewer');
  });

  it('returns to the page the owner left when Exit is pressed', async () => {
    await openPreviewFromCommands();

    // The viewer-capped access answer a guard cached while previewing: it must not decide the way back.
    queryClient.setQueryData(['access-while-previewing'], ['chat:viewer:read']);

    document.body.querySelector<HTMLElement>('[data-slot="view-as-exit"]')!.click();
    await flushPromises();

    expect(queryClient.getQueryData(['access-while-previewing'])).toBeUndefined();
    expect(router.currentRoute.value.fullPath).toBe('/app/u/stoney_eagle/commands');
    expect(viewAs.value).toBe('');
    expect(document.body.querySelector('[data-slot="view-as-banner"]')).toBeNull();
  });
});
