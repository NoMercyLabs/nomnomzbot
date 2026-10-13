// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { VisArea, VisGroupedBar, VisLine, VisStackedBar } from '@unovis/vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeAll, describe, expect, it } from 'vitest'

import ChartPanel, {
  type ChartKind,
  type ChartRow,
  type ChartSeries,
} from '@/components/layout/ChartPanel.vue'
import { i18n } from '@/i18n'
import nl from '@/i18n/locales/nl.json'

beforeAll(() => {
  i18n.global.mergeLocaleMessage('en', { test: { seen: 'Seen', fresh: 'New' } })
  i18n.global.mergeLocaleMessage('nl', { ...nl, test: { seen: 'Gezien', fresh: 'Nieuw' } })
  // jsdom has no text layout; Unovis measures axis labels with this.
  Object.defineProperty(SVGElement.prototype, 'getComputedTextLength', {
    value: () => 40,
    configurable: true,
  })
  Object.defineProperty(SVGElement.prototype, 'getBBox', {
    value: () => ({ x: 0, y: 0, width: 40, height: 12 }),
    configurable: true,
  })
})

afterEach(() => {
  i18n.global.locale.value = 'en'
})

const rows: ChartRow[] = [
  { day: new Date(2026, 0, 5), seen: 1234, fresh: 56 },
  { day: new Date(2026, 0, 6), seen: 2500.5, fresh: 78 },
  { day: new Date(2026, 0, 7), seen: 3100, fresh: 90 },
]
const twoSeries: ChartSeries[] = [
  { key: 'seen', labelKey: 'test.seen', color: 'chart-1' },
  { key: 'fresh', labelKey: 'test.fresh', color: 'chart-2' },
]

function chart(kind: ChartKind, extra: Record<string, unknown> = {}): VueWrapper {
  return mount(ChartPanel, {
    props: {
      title: 'Growth',
      kind,
      data: rows,
      x: 'day',
      series: twoSeries,
      takeaway: 'You gained 3 follows this week.',
      ...extra,
    },
    global: { plugins: [i18n] },
  })
}

describe('the chart panel', () => {
  it('puts the takeaway above the chart', () => {
    const html = chart('line').html()

    expect(html).toContain('You gained 3 follows this week.')
    expect(html.indexOf('You gained 3 follows this week.')).toBeLessThan(
      html.indexOf('data-slot="chart"'),
    )
  })

  it.each([
    ['line', VisLine],
    ['area', VisArea],
    ['bar', VisStackedBar],
    ['groupedBar', VisGroupedBar],
  ] as const)(
    'draws the %s kind with one Unovis component that holds every series',
    (kind, component) => {
      const wrapper = chart(kind)
      const drawn = wrapper.findAllComponents(component)

      expect(drawn).toHaveLength(1)
      const given: Record<string, unknown> = drawn[0]!.props()

      expect(given.y).toHaveLength(2)
      expect(given.color).toEqual(['var(--color-seen)', 'var(--color-fresh)'])
    },
  )

  it('hides the drawing from screen readers', () => {
    expect(chart('line').find('[data-slot="chart"]').attributes('aria-hidden')).toBe('true')
  })

  it('gives screen readers the same numbers in a table, formatted for the English locale', () => {
    const cells: string[] = chart('line')
      .findAll('table.sr-only td')
      .map((cell) => cell.text())
    const heads: string[] = chart('line')
      .findAll('table.sr-only th[scope="col"]')
      .map((cell) => cell.text())

    expect(heads).toEqual(['Period', 'Seen', 'New'])
    expect(cells).toEqual(['1,234', '56', '2,500.5', '78', '3,100', '90'])
    expect(chart('line').find('table.sr-only tbody th').text()).toBe('Jan 5, 2026')
  })

  it('formats the table for the Dutch locale and translates the series names', () => {
    i18n.global.locale.value = 'nl'
    const wrapper = chart('groupedBar')

    expect(wrapper.findAll('table.sr-only th[scope="col"]').map((cell) => cell.text())).toEqual([
      'Periode',
      'Gezien',
      'Nieuw',
    ])
    expect(wrapper.findAll('table.sr-only td').map((cell) => cell.text())).toEqual([
      '1.234',
      '56',
      '2.500,5',
      '78',
      '3.100',
      '90',
    ])
  })

  it('shows the empty state of the panel card and draws no chart when there is no data', () => {
    const wrapper: VueWrapper = chart('line', { data: [] })

    expect(wrapper.find('[data-slot="panel-empty"]').exists()).toBe(true)
    expect(wrapper.findAllComponents(VisLine)).toHaveLength(0)
    expect(wrapper.find('table').exists()).toBe(false)
  })

  it('passes loading and error to the panel card and keeps the takeaway out of them', async () => {
    const loading: VueWrapper = chart('line', { state: 'loading' })
    const failed: VueWrapper = chart('line', { state: 'error' })

    expect(loading.find('[data-slot="panel-loading"]').exists()).toBe(true)
    expect(loading.findAllComponents(VisLine)).toHaveLength(0)
    expect(failed.find('[data-slot="panel-error"]').exists()).toBe(true)
    await failed.find('[data-slot="panel-error"] button').trigger('click')
    expect(failed.emitted('retry')).toHaveLength(1)
  })
})
