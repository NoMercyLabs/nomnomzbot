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
import { defineComponent, h } from 'vue';

import { i18n } from '@/i18n';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

const words = new Map<string, string>([
  ['en', 'Whole word, any case'],
  ['nl', 'Heel woord, elke hoofdletter'],
]);

// A picker whose items are translated words, as on every page that lists kinds, modes or roles.
const Picker = defineComponent({
  setup() {
    return () =>
      h(Select, { modelValue: 'word' }, () => [
        h(SelectTrigger, null, () =>
          h(SelectValue)),
        h(SelectContent, null, () =>
          h(SelectItem, { value: 'word' }, () =>
            words.get(i18n.global.locale.value))),
      ]);
  },
});

describe('a picker with translated items', () => {
  afterEach(() => {
    i18n.global.locale.value = 'en';
    document.body.innerHTML = '';
  });

  it('shows its selected item in the language chosen after it mounted', async () => {
    i18n.global.locale.value = 'nl';

    const picker = mount(Picker, { attachTo: document.body, global: { plugins: [i18n] } });

    await flushPromises();

    expect(picker.find('[data-slot="select-trigger"]').text()).toBe('Heel woord, elke hoofdletter');

    i18n.global.locale.value = 'en';
    await flushPromises();

    expect(picker.find('[data-slot="select-trigger"]').text()).toBe('Whole word, any case');
    picker.unmount();
  });
});
