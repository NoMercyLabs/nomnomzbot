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

import type { Widget } from '@/api/widgets';

// '' is the plain gray dot (switched off); ok is green, warn orange, bad red: the classes of .dot in frame.css.
export type HealthDot = '' | 'ok' | 'warn' | 'bad';

// Truthful states only: the bot reports an error string and whether a source is open, not a load state.
export function overlayHealth(widget: Pick<Widget, 'isEnabled' | 'lastRuntimeError' | 'isAttached'>): HealthDot {
  if (!widget.isEnabled)
    return '';

  if (widget.lastRuntimeError)
    return 'bad';

  return widget.isAttached
    ? 'ok'
    : 'warn';
}
