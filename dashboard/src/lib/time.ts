// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { keysOf, merge } from '@/lib/array';

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

// How long a run or a step took: "350 ms" under a second, "1.2 sec" from there on.
export function durationText(milliseconds: number, locale: string): string {
  const short = milliseconds < 1000;
  const format = new Intl.NumberFormat(locale, {
    style: 'unit',
    unit: short
      ? 'millisecond'
      : 'second',
    unitDisplay: 'short',
    maximumFractionDigits: short
      ? 0
      : 1,
  });

  return format.format(short
    ? milliseconds
    : milliseconds / 1000);
}

// A stream length in hours and minutes: "6 hr 46 min", "45 min" under an hour.
export function hoursMinutes(seconds: number, locale: string): string {
  const minutes = Math.round(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const unit = (value: number, name: 'hour' | 'minute'): string =>
    new Intl.NumberFormat(locale, {
      style: 'unit',
      unit: name,
      unitDisplay: 'short',
    }).format(value);

  if (hours === 0)
    return unit(minutes, 'minute');

  if (minutes % 60 === 0)
    return unit(hours, 'hour');

  return `${unit(hours, 'hour')} ${unit(minutes % 60, 'minute')}`;
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
  return formatTime(iso, 'date', locale);
}

const MINUTE_MS = 60_000;

// The value a datetime-local input shows for a moment: the viewer's own clock, to the minute, no zone.
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso)
    return '';

  const moment = new Date(iso);
  const shifted = new Date(moment.getTime() - moment.getTimezoneOffset() * MINUTE_MS);

  return shifted.toISOString().slice(0, 16);
}

// The moment a datetime-local value means on the viewer's clock, as the ISO text the API takes.
export function fromLocalInput(value: string): string {
  return new Date(value).toISOString();
}

// Whole minutes between two ISO moments.
export function minutesBetween(startIso: string, endIso: string): number {
  return Math.round((new Date(endIso).getTime() - new Date(startIso).getTime()) / MINUTE_MS);
}

export type TimeValue = Date | string | number | null | undefined;

// Every look a moment can have on screen. Add a look here; never build a formatter at a call site.
const DATE_VARIANTS = {
  date: { dateStyle: 'medium' },
  dateShort: { day: 'numeric', month: 'short' },
  time: { hour: 'numeric', minute: '2-digit' },
  timeSeconds: {
    hour: 'numeric',
    minute: '2-digit',
    second: '2-digit',
  },
  dateTime: { dateStyle: 'medium', timeStyle: 'short' },
  dateTimeShort: {
    day: 'numeric',
    month: 'short',
    hour: 'numeric',
    minute: '2-digit',
  },
  dateTimeFull: { dateStyle: 'long', timeStyle: 'medium' },
  weekday: { weekday: 'long' },
  monthYear: { month: 'long', year: 'numeric' },
} as const satisfies Record<string, Intl.DateTimeFormatOptions>;

// `relative` and `iso` read a moment; `duration` and `durationClock` read a length in seconds.
const OTHER_VARIANTS = ['relative', 'iso', 'duration', 'durationClock'] as const;

export type TimeVariant = keyof typeof DATE_VARIANTS | (typeof OTHER_VARIANTS)[number];

// Every variant, built from the tables above so a new look cannot be left out of the composables.
export const TIME_VARIANTS: readonly TimeVariant[] = merge(keysOf(DATE_VARIANTS), OTHER_VARIANTS);

const RELATIVE_UNITS = [
  ['year', 31_536_000],
  ['month', 2_592_000],
  ['week', 604_800],
  ['day', 86_400],
  ['hour', 3_600],
  ['minute', 60],
  ['second', 1],
] as const satisfies readonly (readonly [Intl.RelativeTimeFormatUnit, number])[];

const dateFormats = new Map<string, Intl.DateTimeFormat>();
const relativeFormats = new Map<string, Intl.RelativeTimeFormat>();

function dateFormat(locale: string, variant: keyof typeof DATE_VARIANTS): Intl.DateTimeFormat {
  const key = `${locale}|${variant}`;
  const cached = dateFormats.get(key);

  if (cached)
    return cached;

  const made = new Intl.DateTimeFormat(locale, DATE_VARIANTS[variant]);

  dateFormats.set(key, made);

  return made;
}

function relativeFormat(locale: string): Intl.RelativeTimeFormat {
  const cached = relativeFormats.get(locale);

  if (cached)
    return cached;

  const made = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });

  relativeFormats.set(locale, made);

  return made;
}

// The moment a value means, or null when it is empty or unreadable.
export function toMoment(value: TimeValue): Date | null {
  if (value === null || value === undefined || value === '')
    return null;

  const moment = new Date(value);

  return Number.isNaN(moment.getTime())
    ? null
    : moment;
}

// "in 2 days", "3 minutes ago": the largest whole unit, past or future, in the reader's language.
function relativeText(moment: Date, now: Date, locale: string): string {
  const delta = (moment.getTime() - now.getTime()) / 1000;
  const [unit, size] = RELATIVE_UNITS.find(([, seconds]) => Math.abs(delta) >= seconds) ??
    RELATIVE_UNITS[6];

  return relativeFormat(locale).format(Math.trunc(delta / size) || 0, unit);
}

// A length as a digital clock: "1:02:03", or "2:03" under an hour.
export function clockLength(seconds: number): string {
  const total = Math.max(0, Math.round(seconds));
  const hours = Math.floor(total / 3600);
  const rest = `${twoDigits(Math.floor((total % 3600) / 60))}:${twoDigits(total % 60)}`;

  return hours === 0
    ? rest.replace(/^0/, '')
    : `${hours}:${rest}`;
}

function lengthText(value: TimeValue, variant: 'duration' | 'durationClock', locale: string): string {
  if (typeof value !== 'number' || !Number.isFinite(value))
    return '';

  return variant === 'duration'
    ? hoursMinutes(value, locale)
    : clockLength(value);
}

// One entry for every time on screen, in the locale the person chose (the i18n locale, never the browser's).
// Empty text means the value was empty or unreadable; the caller shows its dash.
export function formatTime(
  value: TimeValue,
  variant: TimeVariant,
  locale: string,
  now: Date = new Date(),
): string {
  if (variant === 'duration' || variant === 'durationClock')
    return lengthText(value, variant, locale);

  const moment = toMoment(value);

  if (!moment)
    return '';

  if (variant === 'iso')
    return moment.toISOString();

  if (variant === 'relative')
    return relativeText(moment, now, locale);

  return dateFormat(locale, variant).format(moment);
}
