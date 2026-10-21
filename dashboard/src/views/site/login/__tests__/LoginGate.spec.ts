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
import { VueQueryPlugin } from '@tanstack/vue-query';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import words from '../i18n/en.json';

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ replace: vi.fn<() => Promise<void>>() }),
}));

const fetched = vi.fn<(input: unknown) => Promise<Response>>(async () =>
  Response.json({ data: [] }));

// Fresh modules each time, like the sign-in page spec, so the page reads the stubbed fetch and flag.
async function mountGate(): Promise<VueWrapper> {
  vi.resetModules();
  const { i18n } = await import('@/i18n');

  i18n.global.mergeLocaleMessage('en', words);
  const { queryClient } = await import('@/api/queryClient');
  const LoginGate = (await import('../LoginGate.vue')).default;

  return mount(LoginGate, {
    global: {
      plugins: [createPinia(), [VueQueryPlugin, { queryClient }], i18n],
      stubs: { RouterLink: { template: '<a><slot /></a>' } },
    },
  });
}

beforeEach(() => {
  fetched.mockClear();
  vi.stubGlobal('fetch', fetched);
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

describe('the login page gate', () => {
  it('shows Coming soon, a disabled button and no sign-in request while sign-in is closed', async () => {
    vi.stubEnv('VITE_SIGN_IN_OPEN', '');
    const wrapper = await mountGate();

    await flushPromises();

    const button = wrapper.find('button[disabled]');

    expect(wrapper.text()).toContain(words.site.login.comingSoon.title);
    expect(button.text()).toBe('Coming soon');
    expect(button.attributes('disabled')).toBeDefined();
    expect(fetched).not.toHaveBeenCalled();
  });

  it('shows the real sign-in page, which asks the bot for its providers, when the flag is on', async () => {
    vi.stubEnv('VITE_SIGN_IN_OPEN', 'true');
    const wrapper = await mountGate();

    await flushPromises();

    expect(wrapper.text()).not.toContain(words.site.login.comingSoon.title);
    await vi.waitFor(() => expect(fetched).toHaveBeenCalled());
  });
});
