// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type VueWrapper } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import FilterBar, { type FilterOption } from '@/components/layout/FilterBar.vue'

function options(count: number): FilterOption[] {
  return Array.from({ length: count }, (_: unknown, index: number) => ({
    value: `v${index}`,
    label: `Option ${index}`,
  }))
}

function bar(count: number, modelValue: string = 'v0'): VueWrapper {
  return mount(FilterBar, { props: { label: 'Status', options: options(count), modelValue } })
}

describe('the filter bar', () => {
  it('shows 2 to 4 options as a segmented control', () => {
    ;[2, 3, 4].forEach((count: number) => {
      const wrapper: VueWrapper = bar(count)
      expect(wrapper.find('[data-slot="toggle-group"]').exists()).toBe(true)
      expect(wrapper.findAll('[data-slot="toggle-group-item"]')).toHaveLength(count)
      expect(wrapper.find('[data-slot="select-trigger"]').exists()).toBe(false)
    })
  })

  it('shows 5 or more options as a select that carries its label', () => {
    const wrapper: VueWrapper = bar(5)

    expect(wrapper.find('[data-slot="toggle-group"]').exists()).toBe(false)
    expect(wrapper.find('[data-slot="select-trigger"]').text()).toContain('Status')
  })

  it('marks the chosen segment and emits the one a person presses', async () => {
    const wrapper: VueWrapper = bar(3, 'v1')
    const items = wrapper.findAll('[data-slot="toggle-group-item"]')

    expect(items[1]!.attributes('data-state')).toBe('on')
    await items[2]!.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toEqual([['v2']])
  })

  it('keeps the filter when the open segment is pressed again', async () => {
    const wrapper: VueWrapper = bar(3, 'v1')

    await wrapper.findAll('[data-slot="toggle-group-item"]')[1]!.trigger('click')

    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })
})
