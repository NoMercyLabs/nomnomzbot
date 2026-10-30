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

import { describe, expect, it } from 'vitest';

import { addMonths, clockLength, formatTime } from '@/lib/time';

process.env.TZ = 'UTC';

const MOMENT: Date = new Date(Date.UTC(2026, 9, 18, 14, 5, 3));

describe('formatTime in the reader locale', () => {
  it('shows the same instant as 12 hour in en and 24 hour in nl', () => {
    expect(formatTime(MOMENT, 'time', 'en')).toBe('2:05 PM');
    expect(formatTime(MOMENT, 'time', 'nl')).toBe('14:05');
    expect(formatTime(MOMENT, 'timeSeconds', 'nl')).toBe('14:05:03');
  });

  it('orders day and month and names them per locale', () => {
    expect(formatTime(MOMENT, 'dateShort', 'en')).toBe('Oct 18');
    expect(formatTime(MOMENT, 'dateShort', 'nl')).toBe('18 okt');
    expect(formatTime(MOMENT, 'date', 'en')).toBe('Oct 18, 2026');
    expect(formatTime(MOMENT, 'weekday', 'nl')).toBe('zondag');
    expect(formatTime(MOMENT, 'monthYear', 'nl')).toBe('oktober 2026');
  });

  it('joins date and time', () => {
    expect(formatTime(MOMENT, 'dateTime', 'nl')).toBe('18 okt 2026, 14:05');
  });

  it('gives the machine value for iso, whatever the locale', () => {
    expect(formatTime(MOMENT, 'iso', 'nl')).toBe('2026-10-18T14:05:03.000Z');
  });

  it('reads an ISO string and an epoch number as the same moment', () => {
    expect(formatTime('2026-10-18T14:05:03Z', 'time', 'nl')).toBe('14:05');
    expect(formatTime(MOMENT.getTime(), 'time', 'nl')).toBe('14:05');
  });

  it('returns empty text for an empty or unreadable value', () => {
    expect(formatTime(null, 'date', 'en')).toBe('');
    expect(formatTime(undefined, 'relative', 'en')).toBe('');
    expect(formatTime('not a date', 'dateTime', 'en')).toBe('');
    expect(formatTime('', 'iso', 'en')).toBe('');
  });
});

describe('forced clocks and short month', () => {
  it('forces a 12 or 24 hour clock whatever the locale would pick', () => {
    const afternoon: Date = new Date(Date.UTC(2026, 9, 18, 14, 5, 0));

    expect(formatTime(afternoon, 'time12', 'nl')).toBe('02:05 p.m.');
    expect(formatTime(afternoon, 'time24', 'en')).toBe('14:05');
  });

  it('writes a weekday with a 24 hour time and a short month with its year', () => {
    expect(formatTime(MOMENT, 'weekdayTime', 'en')).toBe('Sun 14:05');
    expect(formatTime(MOMENT, 'monthYearShort', 'en')).toBe('Oct 2026');
  });
});

describe('relative', () => {
  const now = MOMENT;
  const ago = (seconds: number): Date => new Date(now.getTime() - seconds * 1000);

  it('picks the unit and the direction', () => {
    expect(formatTime(ago(180), 'relative', 'en', now)).toBe('3 minutes ago');
    expect(formatTime(ago(-2 * 86_400), 'relative', 'en', now)).toBe('in 2 days');
    expect(formatTime(ago(7_300), 'relative', 'en', now)).toBe('2 hours ago');
    expect(formatTime(ago(30), 'relative', 'en', now)).toBe('30 seconds ago');
    expect(formatTime(ago(0), 'relative', 'en', now)).toBe('now');
  });

  it('speaks the reader language', () => {
    expect(formatTime(ago(180), 'relative', 'nl', now)).toBe('3 minuten geleden');
    expect(formatTime(ago(-3 * 86_400), 'relative', 'nl', now)).toBe('over 3 dagen');
  });
});

describe('duration', () => {
  it('writes a clock length', () => {
    expect(clockLength(3723)).toBe('1:02:03');
    expect(formatTime(3723, 'durationClock', 'en')).toBe('1:02:03');
    expect(formatTime(123, 'durationClock', 'en')).toBe('2:03');
  });

  it('writes hours and minutes in words', () => {
    expect(formatTime(7500, 'duration', 'en')).toBe('2 hr 5 min');
    expect(formatTime(2700, 'duration', 'nl')).toBe('45 min');
  });

  it('is empty for a value that is not a number of seconds', () => {
    expect(formatTime('x', 'duration', 'en')).toBe('');
    expect(formatTime(Number.NaN, 'durationClock', 'en')).toBe('');
  });
});

describe('addMonths', () => {
  it('moves the same moment N calendar months on, across a year end', () => {
    const from = new Date('2026-10-29T12:00:00Z');

    expect(addMonths(from, 3).toISOString()).toBe('2027-01-29T12:00:00.000Z');
    expect(from.toISOString()).toBe('2026-10-29T12:00:00.000Z');
  });

  it('lands on the last day of a shorter month, as the server does', () => {
    expect(addMonths(new Date('2027-01-31T08:00:00Z'), 1).toISOString()).toBe('2027-02-28T08:00:00.000Z');
    expect(addMonths(new Date('2028-01-31T08:00:00Z'), 1).toISOString()).toBe('2028-02-29T08:00:00.000Z');
  });
});
