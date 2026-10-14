// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import SegmentedControl from '@/components/layout/SegmentedControl.vue';

const options: { value: string; label: string }[] = [
  { value: 'normal', label: 'Normal' },
  { value: 'wide', label: 'Wide' },
  { value: 'wider', label: 'Wider' },
];

function control(modelValue: string): VueWrapper {
  return mount(SegmentedControl, {
    props: {
      label: 'Line spacing',
      options,
      modelValue,
    },
  });
}

describe('the segmented control', () => {
  it('names the group and raises only the chosen option', () => {
    const wrapper = control('wide');
    const items: DOMWrapper<Element>[] = wrapper.findAll('[data-slot="toggle-group-item"]');

    expect(wrapper.find('[data-slot="toggle-group"]').attributes('aria-label')).toBe(
      'Line spacing',
    );

    expect(items.map((item) => item.attributes('data-state'))).toEqual(['off', 'on', 'off']);
  });

  // Stoney 2026-10-14: "iNcOnSisTent" — the page tabs and this control sat side by side in two looks,
  // and an option was squeezed over its own padding. Both now wear the one frame.css tab track.
  it('wears the page tab track and sizes each option to its own label', () => {
    const wrapper = control('wide');
    const group = wrapper.find('[data-slot="toggle-group"]');
    const items: DOMWrapper<Element>[] = wrapper.findAll('[data-slot="toggle-group-item"]');

    expect(group.attributes()).toHaveProperty('data-segmented');
    expect(items.every((item) => 'data-segment' in item.attributes())).toBe(true);
    expect(items.every((item) => item.classes().includes('shrink-0'))).toBe(true);
    expect(group.classes()).not.toContain('auto-cols-fr');
  });

  it('emits the option a person presses and keeps one chosen when the open one is pressed again', async () => {
    const wrapper = control('wide');
    const items: DOMWrapper<Element>[] = wrapper.findAll('[data-slot="toggle-group-item"]');

    await items[1]!.trigger('click');
    expect(wrapper.emitted('update:modelValue')).toBeUndefined();

    await items[2]!.trigger('click');
    expect(wrapper.emitted('update:modelValue')).toEqual([['wider']]);
  });
});
