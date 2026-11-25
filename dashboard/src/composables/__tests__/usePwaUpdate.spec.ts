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

import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, nextTick, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { i18n } from '@/i18n';

import { usePwaUpdate } from '../usePwaUpdate';

const needRefresh = ref(false);
const updateServiceWorker = vi.fn<(reload: boolean) => Promise<void>>(async () => {});
const toast = vi.fn<(...args: unknown[]) => void>();

vi.mock('virtual:pwa-register/vue', () => ({
  useRegisterSW: () => ({ needRefresh, updateServiceWorker }),
}));

vi.mock('vue-sonner', () => ({ toast: (...args: unknown[]): void => toast(...args) }));

const hosts: VueWrapper[] = [];

function mountHost(router: Router = makeRouter()): void {
  const host = mount(
    defineComponent({
      setup() {
        usePwaUpdate(router);

        return () => null;
      },
    }),
    { global: { plugins: [i18n] } },
  );

  hosts.push(host);
}

describe('usePwaUpdate', () => {
  afterEach(() => {
    hosts.splice(0).forEach((host) => host.unmount());
  });

  beforeEach(() => {
    needRefresh.value = false;
    toast.mockClear();
    updateServiceWorker.mockClear();
  });

  it('stays quiet while the cached build is the newest', async () => {
    mountHost();
    await nextTick();

    expect(toast).not.toHaveBeenCalled();
  });

  it('shows one toast that stays until used, and Reload activates the waiting build', async () => {
    mountHost();
    needRefresh.value = true;
    await nextTick();

    expect(toast).toHaveBeenCalledTimes(1);

    const [title, options] = toast.mock.calls[0] as [
      string,
      { duration: number; action: { label: string; onClick: () => void } },
    ];

    expect(title).toBe('A new version is ready');
    expect(options.duration).toBe(Infinity);
    expect(options.action.label).toBe('Reload');

    options.action.onClick();

    expect(updateServiceWorker).toHaveBeenCalledWith(true);
  });

  it('loads a waiting build on the next page change, on the page the user picked', async () => {
    const router = makeRouter();

    await router.push('/a');
    mountHost(router);
    const assign = vi.fn<(url: string) => void>();

    vi.stubGlobal('location', { ...window.location, assign });

    needRefresh.value = true;
    await nextTick();
    await router.push('/b?x=1');

    expect(updateServiceWorker).toHaveBeenCalledWith(false);
    expect(assign).toHaveBeenCalledWith('/b?x=1');
    expect(router.currentRoute.value.path).toBe('/a');
    vi.unstubAllGlobals();
  });

  it('never reloads the open page by itself, so an editor keeps its work', async () => {
    const router = makeRouter();

    await router.push('/a');
    mountHost(router);
    const assign = vi.fn<(url: string) => void>();

    vi.stubGlobal('location', { ...window.location, assign });

    needRefresh.value = true;
    await nextTick();

    expect(updateServiceWorker).not.toHaveBeenCalled();
    expect(assign).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });

  it('changes pages normally while the running build is the newest', async () => {
    const router = makeRouter();

    await router.push('/a');
    mountHost(router);

    await router.push('/b');

    expect(router.currentRoute.value.path).toBe('/b');
    expect(updateServiceWorker).not.toHaveBeenCalled();
  });
});

function makeRouter(): Router {
  const page = defineComponent({ render: () => null });

  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/a', component: page },
      { path: '/b', component: page },
    ],
  });
}
