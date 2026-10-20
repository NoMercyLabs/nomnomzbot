// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
