// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { getLocalTimeZone, parseDate, type CalendarDate } from '@internationalized/date';

import {
  ANALYTICS_RANGES,
  rangeDates,
  type AnalyticsDay,
  type AnalyticsRange,
  type StreamRow,
} from '@/api/analytics';
import type { ChartRow } from '@/components/layout/ChartPanel.vue';
import { formatTime } from '@/lib/time';

export type TopCategory = {
  name: string;
  seconds: number;
  streams: number;
};

function seconds(row: StreamRow): number {
  return Number(row.durationSeconds ?? 0);
}

export function liveSeconds(rows: readonly StreamRow[]): number {
  return rows.reduce((total: number, row) => total + seconds(row), 0);
}

export function averageSeconds(rows: readonly StreamRow[]): number {
  return rows.length === 0
    ? 0
    : liveSeconds(rows) / rows.length;
}

// The category the channel spent the most live time in; a stream without one is not counted.
export function topCategory(rows: readonly StreamRow[]): TopCategory | null {
  const byName = new Map<string, TopCategory>();

  for (const row of rows) {
    if (!row.gameName) continue;

    const known = byName.get(row.gameName) ?? {
      name: row.gameName,
      seconds: 0,
      streams: 0,
    };

    byName.set(row.gameName, {
      ...known,
      seconds: known.seconds + seconds(row),
      streams: known.streams + 1,
    });
  }

  const longest = [...byName.values()]
    .sort((a: TopCategory, b: TopCategory) => b.seconds - a.seconds);

  return longest[0] ?? null;
}

type Slot = {
  at: Date;
  total: number;
};

// Every calendar day of the range, oldest first.
function calendarDays(range: AnalyticsRange): CalendarDate[] {
  const first = parseDate(rangeDates(range).from);

  return Array.from(Array(ANALYTICS_RANGES[range]).keys(), (at: number) =>
    first.add({ days: at }));
}

// One slot per calendar day of the range. A day without a row counts as 0, so every series lines up by date.
// A year is too many bars, so 12 months shows one slot per month.
export function perPeriod(
  days: readonly AnalyticsDay[],
  range: AnalyticsRange,
  value: (day: AnalyticsDay) => number,
  locale: string,
): ChartRow[] {
  const byDate = new Map<string, number>();

  for (const day of days) {
    const key = day.activityDate.slice(0, 10);

    byDate.set(key, (byDate.get(key) ?? 0) + value(day));
  }

  const zone = getLocalTimeZone();
  const slots = calendarDays(range).map((date) => ({
    at: date.toDate(zone),
    total: byDate.get(date.toString()) ?? 0,
  }) as const satisfies Slot);

  if (range !== 'r365') {
    return slots.map((slot) => ({
      period: slot.at,
      value: slot.total,
    }));
  }

  const months = new Map<string, number>();

  for (const slot of slots) {
    const month = formatTime(slot.at, 'monthYearShort', locale);

    months.set(month, (months.get(month) ?? 0) + slot.total);
  }

  return [...months].map(([period, total]) => ({ period, value: total }));
}
