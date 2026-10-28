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

import { formatBytes } from '@/lib/number';
import { formatSpan } from '@/lib/time';

// The bot counts storage in bytes and script time in milliseconds. A person reads megabytes and minutes.
export type LimitUnit = 'count' | 'bytes' | 'milliseconds';

// The unit of a limit follows from its key suffix, so a new storage or time limit reads right on arrival.
export function unitOf(key: string): LimitUnit {
  if (key.endsWith('_bytes'))
    return 'bytes';

  return key.endsWith('_ms')
    ? 'milliseconds'
    : 'count';
}

// "3.2 MB", "30 min", "1,500".
export function formatAmount(value: number, unit: LimitUnit, locale: string): string {
  if (unit === 'bytes')
    return formatBytes(value, locale);

  return unit === 'milliseconds'
    ? formatSpan(value, locale)
    : new Intl.NumberFormat(locale).format(value);
}
