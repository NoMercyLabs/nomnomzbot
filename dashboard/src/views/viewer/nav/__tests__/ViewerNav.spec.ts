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
import { mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createI18n } from 'vue-i18n';

import en from '@/i18n/locales/en.json';
import { viewerRoutes } from '@/router/viewer';

import ViewerNav from '../ViewerNav.vue';

async function mountAt(path: string) {
  const router = createRouter({ history: createMemoryHistory(), routes: [viewerRoutes] });
  const i18n = createI18n({
    legacy: false,
    locale: 'en',
    messages: { en },
  });

  await router.push(path);
  await router.isReady();

  return mount(ViewerNav, { global: { plugins: [router, i18n] } });
}

describe('ViewerNav', () => {
  it('shows one link per page, each under the channel in the address', async () => {
    const wrapper = await mountAt('/me/stoney_eagle/chat');
    const links = wrapper.findAll('a');

    expect(links.map((link) => link.text())).toEqual([
      'My channel',
      'Chat',
      'Now playing',
      'Leaderboards',
      'Points and store',
      'Games',
      'Me',
    ]);

    expect(links.map((link) => link.attributes('href'))).toEqual([
      '/me/stoney_eagle',
      '/me/stoney_eagle/chat',
      '/me/stoney_eagle/now-playing',
      '/me/stoney_eagle/leaderboards',
      '/me/stoney_eagle/points-store',
      '/me/stoney_eagle/games',
      '/me/stoney_eagle/me',
    ]);
  });

  it('shows no page links on /me, where no channel is open yet, instead of failing the whole frame', async () => {
    const wrapper = await mountAt('/me');

    expect(wrapper.findAll('a')).toEqual([]);
  });

  it('marks only the open page as current', async () => {
    const wrapper = await mountAt('/me/stoney_eagle/leaderboards');
    const current = wrapper.findAll('a[aria-current="page"]');

    expect(current.map((link) => link.text())).toEqual(['Leaderboards']);
  });

  it('keeps My channel current on its own address only', async () => {
    const wrapper = await mountAt('/me/stoney_eagle');

    expect(wrapper.findAll('a[aria-current="page"]').map((link) => link.text())).toEqual(['My channel']);
  });

  // Stoney 2026-10-21 (mobile ledger M-12): the phone bar showed a label only under the open page.
  describe('on a phone', () => {
    afterEach(() => {
      vi.unstubAllGlobals();
    });

    it('keeps a short label under every icon, each one fitting a seventh of the bar', async () => {
      vi.stubGlobal('matchMedia', (query: string) => ({
        matches: query.includes('max-width'),
        media: query,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        addListener: () => undefined,
        removeListener: () => undefined,
      }));

      const wrapper = await mountAt('/me/stoney_eagle/chat');
      const labels = wrapper.findAll('a').map((link) => link.text());

      expect(labels).toEqual(['Home', 'Chat', 'Music', 'Ranks', 'Store', 'Games', 'Me']);

      const hidden = wrapper.findAll('a span').filter((span) => span.classes().includes('max-md:sr-only'));

      expect(hidden).toEqual([]);
    });
  });
});
