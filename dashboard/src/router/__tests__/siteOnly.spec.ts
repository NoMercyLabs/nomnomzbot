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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter, RouterView, type Router } from 'vue-router';

import { i18n, setLocale } from '@/i18n';
import { siteOnlyRoutes } from '@/router/siteOnly';
import { followSiteTitle } from '@/router/siteTitle';

import { sitePages } from '../../../vite.site.config';

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

  // The static host answers 404 for a path without its own file, so the build config lists every real page.
  it('lists each of its real pages in the site build config', () => {
    const pages: string[] = (siteOnlyRoutes[0]?.children ?? [])
      .map((child) => child.path)
      .filter((path) => path !== '');

    expect(pages).toEqual([...sitePages]);
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

  it('sets the page title from the home words once they load, and again for the chosen language', async () => {
    const router: Router = createRouter({ history: createMemoryHistory(), routes: siteOnlyRoutes });

    document.title = 'from the HTML';
    followSiteTitle(router);
    await router.push('/');
    mount(Shell, { global: { plugins: [router, i18n] } });
    await flushPromises();

    expect(document.title).toBe('NomNomzBot: run your stream, not your admin work');

    await setLocale('nl');
    await flushPromises();

    expect(document.title).toBe('NomNomzBot: run je stream, niet je administratie');
    await setLocale('en');
  });

  it('shows Coming soon on the login page and makes no request', async () => {
    const { wrapper, router } = await visit('/login');

    expect(router.currentRoute.value.name).toBe('login');
    expect(wrapper.find('button[disabled]').text()).toBe('Coming soon');
    expect(fetched).not.toHaveBeenCalled();
  });
});
