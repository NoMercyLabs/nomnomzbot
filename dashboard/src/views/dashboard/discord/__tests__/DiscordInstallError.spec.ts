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
import { ref } from 'vue';

import { i18n } from '@/i18n';

import DiscordView from '../DiscordView.vue';
import en from '../i18n/en.json';

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({ data: { data: [] }, response: { status: 200 } }),
    POST: async () => ({ data: { data: null }, response: { status: 200 } }),
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  window.history.replaceState({}, '', '/');
});

async function openWith(query: string): Promise<void> {
  window.history.replaceState({}, '', `/app/settings/discord${query}`);

  mounted = mount(DiscordView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

const notice = (): HTMLElement | null => document.body.querySelector('[data-install-error]');

describe('the Discord page after a failed install', () => {
  it.each([
    ['token_exchange_failed', 'client secret'],
    ['access_denied', 'cancel'],
    ['invalid_state', 'expired'],
    ['no_guild', 'server'],
    ['guild_link_failed', 'saving'],
    ['credentials_unconfigured', 'Admin'],
  ])('says in words what went wrong for %s', async (code, word) => {
    await openWith(`?discord_error=${code}`);

    expect(notice()?.textContent?.toLowerCase()).toContain(word.toLowerCase());
    expect(notice()?.textContent).not.toContain(code);
  });

  it('offers to try the install again, and drops the code from the address', async () => {
    await openWith('?discord_error=token_exchange_failed');

    const again = notice()?.querySelector<HTMLAnchorElement>('[data-install-retry]');

    expect(again?.getAttribute('href')).toContain('/channels/c1/integrations/discord/callback/start');
    expect(window.location.search).toBe('');
  });

  it('gives a code it does not know a general message, never the raw code', async () => {
    await openWith('?discord_error=something_new');

    expect(notice()?.textContent).toContain('did not finish');
    expect(notice()?.textContent).not.toContain('something_new');
  });

  it('shows nothing when the install did not fail', async () => {
    await openWith('');

    expect(notice()).toBeNull();
  });
});
