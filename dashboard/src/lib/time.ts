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

function twoDigits(value: number): string {
  return String(value).padStart(2, '0');
}

function localDate(moment: Date): string {
  const month = twoDigits(moment.getMonth() + 1);

  return `${moment.getFullYear()}-${month}-${twoDigits(moment.getDate())}`;
}

// A moment as "22:47" on the same local day as `now`, else "2026-10-14 01:05"; empty when unreadable.
export function clockTime(iso: string | undefined, now: Date): string {
  const moment = new Date(iso ?? '');

  if (Number.isNaN(moment.getTime()))
    return '';

  const clock = `${twoDigits(moment.getHours())}:${twoDigits(moment.getMinutes())}`;

  return localDate(moment) === localDate(now)
    ? clock
    : `${localDate(moment)} ${clock}`;
}
