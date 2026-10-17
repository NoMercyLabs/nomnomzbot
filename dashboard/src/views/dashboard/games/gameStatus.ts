// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { StatusTone } from '@/components/layout/StatusBadge.vue';

// The server may spell the end state either way; the translation table has one key for it.
export function statusKey(status: string): string {
  // cspell:disable-next-line -- the server's status word
  return status.toLowerCase().replace('cancelled', 'canceled');
}

// A lobby or a running game reads live, a game that ended normally reads ok, a canceled one reads off.
export function sessionTone(status: string): StatusTone {
  const normalized = status.toLowerCase();

  if (normalized === 'lobby' || normalized === 'running' || normalized === 'active')
    return 'live';

  if (normalized === 'resolved' || normalized === 'completed')
    return 'ok';

  return 'off';
}
