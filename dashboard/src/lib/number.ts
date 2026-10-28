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

// The API sends an int32 as a number or a numeric string; an absent, empty or non-numeric value is null.
export function numberOrNull(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === '')
    return null;

  const parsed = Number(value);

  return Number.isFinite(parsed)
    ? parsed
    : null;
}

// An amount in cents (the API sends an int32 as a number or a numeric string) as money in the reader's
// language: 1250 and "EUR" read "€12.50" in English.
export function centsAsMoney(cents: number | string, currency: string, locale: string): string {
  const amount = numberOrNull(cents) ?? 0;
  const style = { style: 'currency', currency } as const;

  return new Intl.NumberFormat(locale, style).format(amount / 100);
}

const BYTES_PER_STEP = 1024;

const BYTE_STEPS = [
  ['gigabyte', BYTES_PER_STEP ** 3],
  ['megabyte', BYTES_PER_STEP ** 2],
  ['kilobyte', BYTES_PER_STEP],
  ['byte', 1],
] as const satisfies readonly (readonly [Intl.NumberFormatOptions['unit'], number])[];

// "3.2 MB", "2 GB": the biggest unit in which the size is at least one. Nothing reads "0 MB", not "0 byte".
export function formatBytes(bytes: number, locale: string): string {
  const [unit, size] = bytes === 0
    ? BYTE_STEPS[1]
    : BYTE_STEPS.find(([, stepSize]) => bytes >= stepSize) ?? BYTE_STEPS[3];

  return new Intl.NumberFormat(locale, {
    style: 'unit',
    unit,
    unitDisplay: 'short',
    maximumFractionDigits: 1,
  }).format(bytes / size);
}
