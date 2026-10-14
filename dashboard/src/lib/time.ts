// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

const UNITS = [
  ['day', 86_400],
  ['hour', 3_600],
  ['minute', 60],
  ['second', 1],
] as const satisfies readonly (readonly [Intl.NumberFormatOptions['unit'], number])[];

// A length in its largest whole unit, in the reader's language: "45 sec", "10 min", "14 days".
export function formatLength(seconds: number, locale: string): string {
  const [unit, size] = UNITS.find(([, unitSeconds]) => seconds >= unitSeconds) ?? UNITS[3];
  const format = new Intl.NumberFormat(locale, {
    style: 'unit',
    unit,
    unitDisplay: 'short',
  });

  return format.format(Math.round(seconds / size));
}

// The timeout lengths every timeout menu offers, in seconds: 1 minute, 10 minutes, 1 hour, 1 day.
export const TIMEOUT_LENGTHS = [60, 600, 3600, 86400] as const satisfies readonly number[];

function twoDigits(value: number): string {
  return String(value).padStart(2, '0');
}

function localDate(moment: Date): string {
  const month = twoDigits(moment.getMonth() + 1);

  return `${moment.getFullYear()}-${month}-${twoDigits(moment.getDate())}`;
}

// A moment as "22:47" on the same local day as `now`, else "Oct 14, 01:05" in the reader's language (the year
// only when it is not this year); empty when unreadable.
export function clockTime(iso: string | undefined, now: Date, locale: string): string {
  const moment = new Date(iso ?? '');

  if (Number.isNaN(moment.getTime()))
    return '';

  const clock = `${twoDigits(moment.getHours())}:${twoDigits(moment.getMinutes())}`;

  if (localDate(moment) === localDate(now))
    return clock;

  const day = moment.toLocaleDateString(locale, {
    year: moment.getFullYear() === now.getFullYear()
      ? undefined
      : 'numeric',
    month: 'short',
    day: 'numeric',
  });

  return `${day}, ${clock}`;
}

const AGO_UNITS = [
  ['year', 31_536_000],
  ['month', 2_592_000],
  ['week', 604_800],
  ['day', 86_400],
  ['hour', 3_600],
  ['minute', 60],
] as const satisfies readonly (readonly [Intl.RelativeTimeFormatUnit, number])[];

// A past moment as "3 days ago" in its largest whole unit, in the reader's language; empty when unreadable.
export function agoText(iso: string | undefined, now: Date, locale: string): string {
  const moment = new Date(iso ?? '');

  if (Number.isNaN(moment.getTime()))
    return '';

  const elapsed = (now.getTime() - moment.getTime()) / 1000;
  const seconds = Math.max(0, Math.round(elapsed));
  const [unit, size] = AGO_UNITS.find(([, unitSeconds]) => seconds >= unitSeconds) ?? ['minute', 60];
  const format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });

  return format.format(-Math.floor(seconds / size), unit);
}

// A moment as a short date in the reader's language: "Mar 4, 2020".
export function shortDate(iso: string, locale: string): string {
  return new Date(iso).toLocaleDateString(locale, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}
