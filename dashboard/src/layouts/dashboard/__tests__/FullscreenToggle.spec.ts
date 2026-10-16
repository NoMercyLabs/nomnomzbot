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
import { afterEach, describe, expect, it, vi } from 'vitest';

import { i18n } from '@/i18n';

import FullscreenToggle from '../FullscreenToggle.vue';

function mountToggle(): ReturnType<typeof mount> {
  return mount(FullscreenToggle, { global: { plugins: [i18n] } });
}

describe('FullscreenToggle', () => {
  afterEach(() => {
    Reflect.deleteProperty(document, 'fullscreenEnabled');
    Reflect.deleteProperty(document, 'fullscreenElement');
    Reflect.deleteProperty(document.documentElement, 'requestFullscreen');
  });

  it('shows nothing where the browser has no Fullscreen API', () => {
    expect(mountToggle().find('[data-slot="fullscreen-toggle"]').exists()).toBe(false);
  });

  it('asks the page to go fullscreen, and offers to leave once it did', async () => {
    const enter = vi.fn<() => Promise<void>>(async () => {
      Object.defineProperty(document, 'fullscreenElement', {
        value: document.documentElement,
        configurable: true,
      });

      document.dispatchEvent(new Event('fullscreenchange'));
    });

    Object.defineProperty(document, 'fullscreenEnabled', { value: true, configurable: true });

    Object.defineProperty(document.documentElement, 'requestFullscreen', {
      value: enter,
      configurable: true,
    });

    const wrapper = mountToggle();
    const button = wrapper.get('[data-slot="fullscreen-toggle"]');

    expect(button.text()).toBe('Fullscreen');
    expect(button.attributes('aria-pressed')).toBe('false');

    await button.trigger('click');
    await flushPromises();

    expect(enter).toHaveBeenCalledTimes(1);
    expect(button.text()).toBe('Exit fullscreen');
    expect(button.attributes('aria-pressed')).toBe('true');
  });
});
