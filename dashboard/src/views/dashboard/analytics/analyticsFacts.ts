// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { AnalyticsDay, AnalyticsRange, StreamRow } from '@/api/analytics';
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
  return rows.reduce((total: number, row: StreamRow) => total + seconds(row), 0);
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

// One bar per day; a year is too many bars, so 12 months shows one bar per month.
export function perPeriod(
  days: readonly AnalyticsDay[],
  range: AnalyticsRange,
  value: (day: AnalyticsDay) => number,
  locale: string,
): ChartRow[] {
  if (range !== 'r365') {
    return days.map((day: AnalyticsDay) => ({
      period: new Date(`${day.activityDate}T00:00:00`),
      value: value(day),
    }));
  }

  const totals = new Map<string, number>();

  for (const day of days) {
    const month = formatTime(`${day.activityDate}T00:00:00`, 'monthYearShort', locale);

    totals.set(month, (totals.get(month) ?? 0) + value(day));
  }

  return [...totals].map(([period, total]: [string, number]) => ({ period, value: total }));
}
