// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ChatHourCell } from '@/api/analytics';
import { formatTime } from '@/lib/time';

// The week runs Monday to Sunday; the bot numbers Sunday 0.
export const WEEK = [1, 2, 3, 4, 5, 6, 0] as const satisfies readonly number[];
export const HOURS = Array.from({ length: 24 }, (_: undefined, hour: number) =>
  hour);

export type DayTotal = {
  day: number;
  lines: number;
  busiestHour: number | null;
};

export type HourTotal = {
  hour: number;
  lines: number;
  share: number;
};

function lines(cell: ChatHourCell): number {
  return Number(cell.messages);
}

export function totalLines(cells: readonly ChatHourCell[]): number {
  return cells.reduce((total: number, cell: ChatHourCell) => total + lines(cell), 0);
}

export function linesAt(cells: readonly ChatHourCell[], day: number, hour: number): number {
  const cell = cells.find((item: ChatHourCell) =>
    Number(item.dayOfWeek) === day && Number(item.hour) === hour);

  return cell
    ? lines(cell)
    : 0;
}

// The hour with the most chat over the whole range; null when nobody chatted.
export function busiestCell(cells: readonly ChatHourCell[]): ChatHourCell | null {
  return cells.reduce<ChatHourCell | null>((best: ChatHourCell | null, cell: ChatHourCell) =>
    best === null || lines(cell) > lines(best)
      ? cell
      : best, null);
}

// Each weekday, Monday first, with its lines and the hour it was busiest.
export function dayTotals(cells: readonly ChatHourCell[]): DayTotal[] {
  return WEEK.map((day: number) => {
    const ofDay = cells.filter((cell: ChatHourCell) =>
      Number(cell.dayOfWeek) === day);
    const busiest = busiestCell(ofDay);

    return {
      day,
      lines: totalLines(ofDay),
      busiestHour: busiest && Number(busiest.hour),
    };
  });
}

// The hours of the day with the most chat, most first, each with its part of all chat.
export function topHours(cells: readonly ChatHourCell[], count: number): HourTotal[] {
  const total = totalLines(cells);
  const hours = HOURS.map((hour: number) => {
    const sum = totalLines(cells.filter((cell: ChatHourCell) =>
      Number(cell.hour) === hour));

    return {
      hour,
      lines: sum,
      share: total === 0
        ? 0
        : sum / total,
    };
  });

  return hours
    .filter((item: HourTotal) =>
      item.lines > 0)
    .sort((a: HourTotal, b: HourTotal) => b.lines - a.lines)
    .slice(0, count);
}

// How dark a grid cell is: 0 for no chat, then 1 to 3 by its part of the busiest hour.
export function heatLevel(value: number, most: number): 0 | 1 | 2 | 3 {
  if (value === 0 || most === 0)
    return 0;

  return Math.min(3, Math.ceil((value / most) * 3)) as 1 | 2 | 3;
}

// 4 January 2026 is a Sunday, so the bot's day number is the days after it.
export function dayName(day: number, width: 'short' | 'long', locale: string): string {
  return formatTime(new Date(2026, 0, 4 + day), width === 'long'
    ? 'weekday'
    : 'weekdayShort', locale);
}

// An hour as the reader's clock writes it: "16:00", or "4:00 PM"; short gives "16" or "4 PM".
export function hourName(hour: number, locale: string, short = false): string {
  return formatTime(new Date(2026, 0, 4, hour), short
    ? 'hour'
    : 'time', locale);
}
