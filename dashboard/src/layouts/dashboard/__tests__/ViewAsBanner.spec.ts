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
