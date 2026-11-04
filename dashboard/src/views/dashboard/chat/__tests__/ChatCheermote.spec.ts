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

import { mount } from '@vue/test-utils';
import { expect, it } from 'vitest';

import { i18n } from '@/i18n';
import { chatDisplayDefaults } from '../chatDisplay';
import ChatFragments from '../components/ChatFragments.vue';

it('renders a resolved cheermote image with its text as the accessible name', () => {
  const wrapper = mount(ChatFragments, {
    props: {
      fragments: [{
        type: 'cheermote',
        text: 'Cheer100',
        emote: null,
        mention: null,
        linkUrl: null,
        linkPreview: null,
        gif: null,
        cheermote: {
          prefix: 'Cheer',
          bits: 100,
          tier: 100,
          urls: { 1: 'https://example.test/cheer.png' },
          animated: false,
          colorHex: null,
        },
      }],
      display: chatDisplayDefaults,
      me: null,
    },
    global: { plugins: [i18n] },
  });

  expect(wrapper.find('[data-cheermote]').attributes('src')).toBe('https://example.test/cheer.png');
  expect(wrapper.find('[data-cheermote]').attributes('alt')).toBe('Cheer100');
});
