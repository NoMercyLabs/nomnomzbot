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

import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { describe, expect, it } from 'vitest'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '..'

function render(stacked: boolean) {
  const Host = defineComponent(() => () => h(Table, { stacked }, () => [
    h(TableHeader, () => h(TableRow, () => [h(TableHead, () => 'Name'), h(TableHead, () => 'Ends')])),
    h(TableBody, () => h(TableRow, () => [
      h(TableCell, { stack: 'title' }, () => 'Key one'),
      h(TableCell, { label: 'Ends' }, () => 'Oct 20, 2026'),
      h(TableCell, { stack: 'actions' }, () => h('button', 'Revoke')),
    ])),
  ]))

  return mount(Host)
}

describe('Table stacked below md', () => {
  it('hides the header row and turns each row into a card with no sideways scroll', () => {
    const wrapper = render(true)

    expect(wrapper.get('[data-slot=table-container]').classes()).toContain('max-md:overflow-visible')
    expect(wrapper.get('[data-slot=table-header]').classes()).toContain('max-md:hidden')
    expect(wrapper.get('tbody [data-slot=table-row]').classes()).toEqual(expect.arrayContaining(['max-md:flex', 'max-md:flex-col', 'max-md:border']))
  })

  it('shows the column label beside the value on a phone only, with the value on one line', () => {
    const cell = render(true).findAll('[data-slot=table-cell]')[1]!
    const label = cell.get('[data-cell-label]')

    expect(label.text()).toBe('Ends')
    expect(label.classes()).toContain('md:hidden')
    expect(cell.classes()).toEqual(expect.arrayContaining(['max-md:flex', 'max-md:justify-between']))
    expect(cell.text()).toContain('Oct 20, 2026')
  })

  it('keeps the row actions as a visible button group at the end of the card', () => {
    const actions = render(true).findAll('[data-slot=table-cell]')[2]!

    expect(actions.attributes('data-stack')).toBe('actions')
    expect(actions.classes()).toContain('max-md:justify-end')
    expect(actions.find('button').text()).toBe('Revoke')
  })

  it('leaves a table without stacked unchanged: no labels, no phone classes', () => {
    const wrapper = render(false)

    expect(wrapper.find('[data-cell-label]').exists()).toBe(false)
    expect(wrapper.html()).not.toContain('max-md:')
  })
})
