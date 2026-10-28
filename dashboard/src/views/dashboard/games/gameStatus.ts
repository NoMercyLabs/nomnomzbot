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
