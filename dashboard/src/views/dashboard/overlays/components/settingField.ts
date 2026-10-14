// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { WidgetSettingsField } from '@/api/widgets';
import { numberOrNull } from '@/lib/number';

// Settings values are plain JSON, so two values are the same when they serialize the same.
export function sameValue(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

// Holds a typed number inside the field's min and max; text that is not a number keeps the old value.
export function clampNumber(typed: string, field: WidgetSettingsField, fallback: unknown): unknown {
  const parsed = numberOrNull(typed);

  if (parsed === null)
    return fallback;

  const min = numberOrNull(field.min);
  const max = numberOrNull(field.max);

  if (min !== null && parsed < min)
    return min;

  if (max !== null && parsed > max)
    return max;

  return parsed;
}
