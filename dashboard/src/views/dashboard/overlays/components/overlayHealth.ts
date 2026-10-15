// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Widget } from '@/api/widgets';

// '' is the plain grey dot (switched off); ok is green, warn orange, bad red: the classes of .dot in frame.css.
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
