// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import ViewerLayout from '@/layouts/ViewerLayout.vue';

function mountLayout() {
  return mount(ViewerLayout, {
    global: {
      mocks: { $t: (key: string) => key },
      stubs: {
        AppMark: true,
        LanguagePicker: true,
        ViewerChannelFinder: true,
        ViewerNav: true,
        Toaster: true,
        RouterView: true,
      },
    },
  });
}

describe('ViewerLayout', () => {
  it('insets the brand row and the navigation by the same 24px a page header uses', () => {
    const wrapper = mountLayout();

    expect(wrapper.find('header').classes()).toContain('px-6');
    expect(wrapper.find('[data-slot="viewer-nav-row"]').classes()).toContain('px-6');
  });

  it('leaves the page area without a side inset, so a page header sits flush with the brand', () => {
    const wrapper = mountLayout();
    const sideInsets = wrapper
      .find('main')
      .classes()
      .filter((name) => /^p-\d|^px-\d/.test(name));

    expect(sideInsets).toEqual([]);
  });
});
