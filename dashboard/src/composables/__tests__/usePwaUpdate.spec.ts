// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, nextTick, ref } from 'vue';

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

function mountHost(): void {
  const host = mount(
    defineComponent({
      setup() {
        usePwaUpdate();

        return () => null;
      },
    }),
    { global: { plugins: [i18n] } },
  );

  hosts.push(host);
}

describe('usePwaUpdate', () => {
  afterEach(() => {
    hosts.splice(0).forEach((host: VueWrapper) => host.unmount());
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
});
