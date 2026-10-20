// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The bot counts storage in bytes and script time in milliseconds. A person reads megabytes and minutes.
export type LimitUnit = 'count' | 'bytes' | 'milliseconds';

type ReadableUnit = {
  unit: Intl.NumberFormatOptions['unit'];
  size: number;
};

const bytesPerKilobyte = 1024;
const millisecondsPerSecond = 1000;

const byteSteps = [
  { unit: 'gigabyte', size: bytesPerKilobyte ** 3 },
  { unit: 'megabyte', size: bytesPerKilobyte ** 2 },
  { unit: 'kilobyte', size: bytesPerKilobyte },
  { unit: 'byte', size: 1 },
] as const satisfies readonly ReadableUnit[];

const timeSteps = [
  { unit: 'hour', size: millisecondsPerSecond * 3600 },
  { unit: 'minute', size: millisecondsPerSecond * 60 },
  { unit: 'second', size: millisecondsPerSecond },
  { unit: 'millisecond', size: 1 },
] as const satisfies readonly ReadableUnit[];

// The unit of a limit follows from its key suffix, so a new storage or time limit reads right on arrival.
export function unitOf(key: string): LimitUnit {
  if (key.endsWith('_bytes'))
    return 'bytes';

  return key.endsWith('_ms')
    ? 'milliseconds'
    : 'count';
}

function stepFor(value: number, steps: readonly ReadableUnit[]): ReadableUnit {
  const fallback = steps[steps.length - 1]!;

  return steps.find((step) => value >= step.size) ?? fallback;
}

function formatIn(value: number, step: ReadableUnit, locale: string): string {
  return new Intl.NumberFormat(locale, {
    style: 'unit',
    unit: step.unit,
    unitDisplay: 'short',
    maximumFractionDigits: 1,
  }).format(value / step.size);
}

// "3.2 MB", "30 min", "1,500": the biggest unit in which the value is at least one.
export function formatAmount(value: number, unit: LimitUnit, locale: string): string {
  if (unit === 'count')
    return new Intl.NumberFormat(locale).format(value);

  const step = stepFor(value, unit === 'bytes'
    ? byteSteps
    : timeSteps);

  return formatIn(value, step, locale);
}
