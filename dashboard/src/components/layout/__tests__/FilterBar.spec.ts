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

import { mount, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import FilterBar from '@/components/layout/FilterBar.vue';
import type { FilterOption } from '@/components/layout/filterOption';

function options(count: number): FilterOption[] {
  return Array.from({ length: count }, (_: unknown, index: number) => ({
    value: `v${index}`,
    label: `Option ${index}`,
  }));
}

function bar(count: number, modelValue: string = 'v0'): VueWrapper {
  return mount(FilterBar, {
    props: {
      label: 'Status',
      options: options(count),
      modelValue,
    },
  });
}

describe('the filter bar', () => {
  it('shows 2 to 4 options as a segmented control', () => {
    [2, 3, 4].forEach((count) => {
      const wrapper = bar(count);

      expect(wrapper.find('[data-slot="toggle-group"]').exists()).toBe(true);
      expect(wrapper.findAll('[data-slot="toggle-group-item"]')).toHaveLength(count);
      expect(wrapper.find('[data-slot="select-trigger"]').exists()).toBe(false);
    });
  });

  it('shows 5 or more options as a select that carries its label', () => {
    const wrapper = bar(5);

    expect(wrapper.find('[data-slot="toggle-group"]').exists()).toBe(false);
    expect(wrapper.find('[data-slot="select-trigger"]').text()).toContain('Status');
  });

  it('marks the chosen segment and emits the one a person presses', async () => {
    const wrapper: VueWrapper = bar(3, 'v1');
    const items = wrapper.findAll('[data-slot="toggle-group-item"]');

    expect(items[1]!.attributes('data-state')).toBe('on');
    await items[2]!.trigger('click');

    expect(wrapper.emitted('update:modelValue')).toEqual([['v2']]);
  });

  it('keeps the filter when the open segment is pressed again', async () => {
    const wrapper: VueWrapper = bar(3, 'v1');

    await wrapper.findAll('[data-slot="toggle-group-item"]')[1]!.trigger('click');

    expect(wrapper.emitted('update:modelValue')).toBeUndefined();
  });
});
