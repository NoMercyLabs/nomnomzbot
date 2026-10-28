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

import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { nextTick } from 'vue';

import { viewAs } from '@/api/viewAs';
import { i18n } from '@/i18n';

import ViewAsBanner from '../ViewAsBanner.vue';

afterEach(() => {
  viewAs.value = '';
  localStorage.clear();
});

describe('the Viewing as banner', () => {
  it('is absent when the owner sees the full dashboard', () => {
    const banner = mount(ViewAsBanner, { global: { plugins: [i18n] } });

    expect(banner.find('[data-slot="view-as-banner"]').exists()).toBe(false);
  });

  it('names the previewed role, and Exit brings the full dashboard back', async () => {
    viewAs.value = 'moderator';
    const banner = mount(ViewAsBanner, { global: { plugins: [i18n] } });

    expect(banner.text()).toContain('Viewing as a moderator');

    await banner.find('[data-slot="view-as-exit"]').trigger('click');
    await flushPromises();
    await nextTick();

    expect(viewAs.value).toBe('');
    expect(banner.find('[data-slot="view-as-banner"]').exists()).toBe(false);
  });
});
