// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
