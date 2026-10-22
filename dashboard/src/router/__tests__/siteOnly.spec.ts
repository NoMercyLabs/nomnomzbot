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
import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter, RouterView, type Router } from 'vue-router';

import { i18n } from '@/i18n';
import { siteOnlyRoutes } from '@/router/siteOnly';

const Shell = defineComponent({ components: { RouterView }, template: '<RouterView />' });

const fetched = vi.fn<(input: unknown) => Promise<Response>>(async () => Response.json({}));

async function visit(path: string): Promise<{ wrapper: VueWrapper; router: Router }> {
  const router: Router = createRouter({ history: createMemoryHistory(), routes: siteOnlyRoutes });

  await router.push(path);
  const wrapper: VueWrapper = mount(Shell, { global: { plugins: [router, i18n] } });

  await flushPromises();

  return { wrapper, router };
}

beforeEach(() => {
  fetched.mockClear();
  vi.stubGlobal('fetch', fetched);
  vi.stubEnv('VITE_SIGN_IN_OPEN', 'false');
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('the public site router', () => {
  it('has the home page, the login page and the not-found page, and nothing else', () => {
    const router: Router = createRouter({ history: createMemoryHistory(), routes: siteOnlyRoutes });
    const names = router
      .getRoutes()
      .map((route) => String(route.name ?? ''))
      .filter(Boolean);

    expect(names.sort()).toEqual(['home', 'login', 'not-found']);
  });

  it.each(['/app', '/app/u/someone/commands', '/setup'])(
    'sends %s back to the home page',
    async (path) => {
      const { router } = await visit(path);

      expect(router.currentRoute.value.fullPath).toBe('/');
      expect(router.currentRoute.value.name).toBe('home');
    },
  );

  it('shows the not-found page for an unknown path', async () => {
    const { wrapper, router } = await visit('/does-not-exist');

    expect(router.currentRoute.value.name).toBe('not-found');
    expect(wrapper.find('h2').text()).toBe('This page does not exist');
  });

  it('puts Coming soon on every sign-in button of the home page and makes no request', async () => {
    const { wrapper } = await visit('/');
    const signIn = wrapper.findAll('button').filter((button) => button.text() === 'Coming soon');

    expect(signIn.length).toBeGreaterThanOrEqual(2);
    expect(signIn.every((button) => button.attributes('disabled') !== undefined)).toBe(true);
    expect(fetched).not.toHaveBeenCalled();
  });

  it('shows Coming soon on the login page and makes no request', async () => {
    const { wrapper, router } = await visit('/login');

    expect(router.currentRoute.value.name).toBe('login');
    expect(wrapper.find('button[disabled]').text()).toBe('Coming soon');
    expect(fetched).not.toHaveBeenCalled();
  });
});
