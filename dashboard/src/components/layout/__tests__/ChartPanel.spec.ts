// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { VisArea, VisAxis, VisGroupedBar, VisLine, VisStackedBar } from '@unovis/vue';
import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeAll, describe, expect, it } from 'vitest';

import ChartPanel, {
  type ChartKind,
  type ChartRow,
  type ChartSeries,
} from '@/components/layout/ChartPanel.vue';
import { i18n } from '@/i18n';
import nl from '@/i18n/locales/nl.json';

beforeAll(() => {
  i18n.global.mergeLocaleMessage('en', { test: { seen: 'Seen', fresh: 'New' } });
  i18n.global.mergeLocaleMessage('nl', { ...nl, test: { seen: 'Gezien', fresh: 'Nieuw' } });
});

afterEach(() => {
  i18n.global.locale.value = 'en';
});

const rows: ChartRow[] = [
  {
    day: new Date(2026, 0, 5),
    seen: 1234,
    fresh: 56,
  },
  {
    day: new Date(2026, 0, 6),
    seen: 2500.5,
    fresh: 78,
  },
  {
    day: new Date(2026, 0, 7),
    seen: 3100,
    fresh: 90,
  },
];
const twoSeries: ChartSeries[] = [
  {
    key: 'seen',
    labelKey: 'test.seen',
    color: 'chart-1',
  },
  {
    key: 'fresh',
    labelKey: 'test.fresh',
    color: 'chart-2',
  },
];

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
  });
}

describe('the chart panel', () => {
  it('puts the takeaway above the chart', () => {
    const html = chart('line').html();

    expect(html).toContain('You gained 3 follows this week.');

    expect(html.indexOf('You gained 3 follows this week.')).toBeLessThan(
      html.indexOf('data-slot="chart"'),
    );
  });

  it.each([
    ['line', VisLine],
    ['area', VisArea],
    ['bar', VisStackedBar],
    ['groupedBar', VisGroupedBar],
  ] as const)(
    'draws the %s kind with one Unovis component that holds every series',
    (kind, component) => {
      const wrapper = chart(kind);
      const drawn = wrapper.findAllComponents(component);

      expect(drawn).toHaveLength(1);
      const given: Record<string, unknown> = drawn[0]!.props();

      expect(given.y).toHaveLength(2);
      expect(given.color).toEqual(['var(--color-seen)', 'var(--color-fresh)']);
    },
  );

  it('labels a 90 bar chart on at most 8 evenly spaced bars and names the first and each step', () => {
    const many: ChartRow[] = Array.from({ length: 90 }, (_unused: unknown, at: number) => ({
      day: new Date(2026, 6, 21 + at),
      seen: at,
      fresh: 0,
    }));
    const wrapper = chart('bar', { data: many });
    const drawn = wrapper.findAllComponents(VisAxis);
    const axes = drawn.map((one): Record<string, unknown> => one.props());
    const ticks = axes.find((given) => given.type === 'x')?.tickValues as number[];

    expect(ticks.length).toBeLessThanOrEqual(8);
    expect(ticks[0]).toBe(0);
    expect(ticks.every((tick) => Number.isInteger(tick) && tick < 90)).toBe(true);
  });

  it('hides the drawing from screen readers', () => {
    expect(chart('line').find('[data-slot="chart"]').attributes('aria-hidden')).toBe('true');
  });

  // A table ignores sr-only's 1px height, so on the table itself it stayed 360px tall and scrolled the whole
  // page (owner 2026-10-14: "the whole page scrolls too"). The wrapper div clips it.
  it('hides the screen-reader table inside a clipping wrapper, never on the table itself', () => {
    expect(chart('line').find('table').classes()).not.toContain('sr-only');
    expect(chart('line').find('table').element.parentElement?.classList).toContain('sr-only');
  });

  it('gives screen readers the same numbers in a table, formatted for the English locale', () => {
    const cells: string[] = chart('line')
      .findAll('div.sr-only > table td')
      .map((cell) => cell.text());
    const heads: string[] = chart('line')
      .findAll('div.sr-only > table th[scope="col"]')
      .map((cell) => cell.text());

    expect(heads).toEqual(['Period', 'Seen', 'New']);
    expect(cells).toEqual(['1,234', '56', '2,500.5', '78', '3,100', '90']);
    expect(chart('line').find('div.sr-only > table tbody th').text()).toBe('Jan 5, 2026');
  });

  it('formats the table for the Dutch locale and translates the series names', () => {
    i18n.global.locale.value = 'nl';
    const wrapper = chart('groupedBar');

    expect(wrapper.findAll('div.sr-only > table th[scope="col"]').map((cell) => cell.text())).toEqual([
      'Periode',
      'Gezien',
      'Nieuw',
    ]);

    expect(wrapper.findAll('div.sr-only > table td').map((cell) => cell.text())).toEqual([
      '1.234',
      '56',
      '2.500,5',
      '78',
      '3.100',
      '90',
    ]);
  });

  it('shows the empty state of the panel card and draws no chart when there is no data', () => {
    const wrapper: VueWrapper = chart('line', { data: [] });

    expect(wrapper.find('[data-slot="panel-empty"]').exists()).toBe(true);
    expect(wrapper.findAllComponents(VisLine)).toHaveLength(0);
    expect(wrapper.find('table').exists()).toBe(false);
  });

  it('passes loading and error to the panel card and keeps the takeaway out of them', async () => {
    const loading: VueWrapper = chart('line', { state: 'loading' });
    const failed: VueWrapper = chart('line', { state: 'error' });

    expect(loading.find('[data-slot="panel-loading"]').exists()).toBe(true);
    expect(loading.findAllComponents(VisLine)).toHaveLength(0);
    expect(failed.find('[data-slot="panel-error"]').exists()).toBe(true);
    await failed.find('[data-slot="panel-error"] button').trigger('click');
    expect(failed.emitted('retry')).toHaveLength(1);
  });
});
