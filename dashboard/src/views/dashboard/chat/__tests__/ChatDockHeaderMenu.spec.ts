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
import { createI18n } from 'vue-i18n';

import ChatDockHeader from '../components/ChatDockHeader.vue';
import en from '../i18n/en.json';

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: { en },
});

describe('ChatDockHeader more menu', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('ends each item that opens a panel with a chevron', async () => {
    const wrapper = mount(ChatDockHeader, {
      props: {
        title: 'Chat',
        live: true,
        statusLabel: 'Live',
        countLabel: '1',
        clearFocusLabel: null,
        showFrameActions: false,
        searchOpen: false,
        modToolsOpen: false,
        identityOpen: false,
        displayOpen: false,
      },
      global: { plugins: [i18n] },
      attachTo: document.body,
    });

    await wrapper.get('[data-dock-more]').trigger('keydown', { key: 'Enter' });
    await flushPromises();
    const items = Array.from(document.body.querySelectorAll('[data-more-mod-tools], [data-more-identity], [data-more-display]'));

    expect(items).toHaveLength(3);

    for (const item of items) {
      expect(item.querySelector('[data-menu-chevron]')).not.toBeNull();
    }
  });
});
