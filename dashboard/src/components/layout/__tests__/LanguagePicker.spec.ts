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
import { afterEach, describe, expect, it } from 'vitest';

import LanguagePicker from '@/components/layout/LanguagePicker.vue';
import { i18n } from '@/i18n';

afterEach(() => {
  document.body.innerHTML = '';
});

// Stoney 2026-10-13: the open list covered the picker and the button beside it.
describe('the language picker', () => {
  it('opens its list below the button, not over it', async () => {
    const wrapper: VueWrapper = mount(LanguagePicker, {
      attachTo: document.body,
      global: { plugins: [i18n] },
    });

    await wrapper.find('[data-slot="select-trigger"]').trigger('keydown', { key: 'Enter' });
    await flushPromises();

    const list: HTMLElement | null = document.querySelector('[data-slot="select-content"]');

    expect(list).not.toBeNull();
    expect(list!.getAttribute('data-align-trigger')).toBe('false');
    expect(list!.getAttribute('data-side')).toBe('bottom');
  });

  // Stoney 2026-10-13 (translated from Dutch): "There is no margin around the buttons." This shadcn style pads the group, not the list.
  it('keeps its options inside the padded group, off the list edge', async () => {
    const wrapper: VueWrapper = mount(LanguagePicker, {
      attachTo: document.body,
      global: { plugins: [i18n] },
    });

    await wrapper.find('[data-slot="select-trigger"]').trigger('keydown', { key: 'Enter' });
    await flushPromises();
    const options = document.querySelectorAll<Element>('[data-slot="select-item"]');

    expect(options.length).toBeGreaterThan(0);

    options.forEach((option: Element) =>
      expect(option.closest('[data-slot="select-group"]')).not.toBeNull(),
    );
  });
});
