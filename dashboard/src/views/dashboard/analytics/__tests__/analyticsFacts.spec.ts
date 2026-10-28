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

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { AnalyticsDay } from '@/api/analytics';
import type { ChartRow } from '@/components/layout/ChartPanel.vue';

import { perPeriod } from '../analyticsFacts';

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date(2026, 9, 18, 12));
});

afterEach(() => {
  vi.useRealTimers();
});

const day = (activityDate: string, newFollowers: number, uniqueChatters: number): AnalyticsDay =>
  ({
    activityDate,
    newFollowers,
    uniqueChatters,
  }) as AnalyticsDay;

const sparse: AnalyticsDay[] = [
  day('2026-07-21', 2, 5),
  day('2026-07-25', 1, 0),
  day('2026-07-28', 4, 9),
  day('2026-09-14', 3, 2),
  day('2026-10-18', 7, 1),
];

const followers = (row: AnalyticsDay): number => Number(row.newFollowers);
const chatters = (row: AnalyticsDay): number => Number(row.uniqueChatters);
const dates = (rows: ChartRow[]): number[] =>
  rows.map((row: ChartRow) => (row.period as Date).getTime());

describe('perPeriod', () => {
  it('gives one slot per calendar day for a sparse 90 day range', () => {
    const rows = perPeriod(sparse, 'r90', followers, 'en');

    expect(rows).toHaveLength(90);
    expect(new Set(dates(rows)).size).toBe(90);
  });

  it('fills the days the bot has no row for with zero and keeps the days it has', () => {
    const rows = perPeriod(sparse, 'r90', followers, 'en');
    const byDay = new Map(rows.map((row: ChartRow) => [(row.period as Date).getTime(), row.value]));

    expect(rows.filter((row: ChartRow) => row.value === 0)).toHaveLength(85);
    expect(byDay.get(new Date(2026, 6, 28).getTime())).toBe(4);
  });

  it('aligns two series by date, even when their sparse days differ', () => {
    const one = perPeriod([day('2026-09-14', 3, 0)], 'r90', followers, 'en');
    const other = perPeriod([day('2026-08-02', 0, 6)], 'r90', chatters, 'en');

    expect(dates(one)).toEqual(dates(other));
    expect(one.at(-1)?.period).toEqual(new Date(2026, 9, 18));
  });

  it('gives 30 slots for 30 days', () => {
    expect(perPeriod([], 'r30', followers, 'en')).toHaveLength(30);
  });

  it('gives one slot per month for 12 months, empty months included', () => {
    const rows = perPeriod([day('2026-03-02', 5, 1)], 'r365', followers, 'en');
    const periods = rows.map((row: ChartRow) => row.period);

    expect(rows.length).toBeGreaterThanOrEqual(12);
    expect(rows.length).toBeLessThanOrEqual(13);
    expect(new Set(periods).size).toBe(rows.length);
    expect(rows.filter((row: ChartRow) => row.value === 5)).toHaveLength(1);
  });
});
