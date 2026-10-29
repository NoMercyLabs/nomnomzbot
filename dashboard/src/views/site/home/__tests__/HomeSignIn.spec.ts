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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import words from '../i18n/en.json';
import HomeSignIn from '../HomeSignIn.vue';

const held = vi.hoisted(() => ({ signedIn: false, restores: 0 }));

vi.mock('@/api/auth', () => ({
  useSignedIn: () => ref(held.signedIn),
  restoreSession: () => {
    held.restores += 1;

    return Promise.resolve(held.signedIn);
  },
}));

const page = defineComponent({ render: () => null });

async function show(withDashboard: boolean = true): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: page },
      {
        path: '/app',
        name: withDashboard
          ? 'dashboard-home'
          : 'app',
        component: page,
      },
      {
        path: '/login',
        name: 'login',
        component: page,
      },
    ],
  });
  const wrapper: VueWrapper = mount(HomeSignIn, { global: { plugins: [router, i18n] } });

  await flushPromises();

  return wrapper;
}

beforeAll(() => {
  // The home page words load with the page route; a component test loads them itself.
  i18n.global.mergeLocaleMessage('en', words);
});

beforeEach(() => {
  held.signedIn = false;
  held.restores = 0;
});

describe('the home page sign-in action', () => {
  it('opens the dashboard for a person who is already signed in', async () => {
    held.signedIn = true;

    const link = (await show()).find('a');

    expect(link.text()).toBe(words.site.home.openDashboard);
    expect(link.attributes('href')).toBe('/app');
  });

  it('asks for the stored session once, so a returning person is recognized', async () => {
    await show();

    expect(held.restores).toBe(1);
  });

  it('makes no request and offers no button on the static site, which has no dashboard behind it', async () => {
    held.signedIn = true;

    const wrapper = await show(false);

    expect(held.restores).toBe(0);
    expect(wrapper.findAll('a').some((link) => link.attributes('href') === '/app')).toBe(false);
  });

  it('offers no dashboard link to a visitor who is not signed in', async () => {
    const wrapper = await show();

    expect(wrapper.findAll('a').some((link) => link.attributes('href') === '/app')).toBe(false);
  });
});
