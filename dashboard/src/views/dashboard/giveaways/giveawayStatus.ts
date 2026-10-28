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

import type { Giveaway, GiveawayAction } from '@/api/giveaways';
import type { StatusTone } from '@/components/layout/StatusBadge.vue';

// Taking entries reads live, a drawn giveaway reads ok, a draft, a closed or an archived one reads off.
export function statusTone(status: string): StatusTone {
  if (status === 'open')
    return 'live';

  if (status === 'drawn')
    return 'ok';

  return 'off';
}

// The steps that fit a status, in button order. The last one is the step the page leads with.
export function actionsFor(status: string): GiveawayAction[] {
  if (status === 'draft')
    return ['open'];

  if (status === 'open')
    return ['close', 'draw'];

  if (status === 'closed')
    return ['draw'];

  return [];
}

// The giveaway whose winners the page shows: the one taking entries, else the one waiting for a draw,
// else the one drawn last.
export function focusGiveaway(items: readonly Giveaway[]): Giveaway | undefined {
  return (
    items.find((item) => item.status === 'open') ??
    items.find((item) => item.status === 'closed') ??
    items.find((item) => item.status === 'drawn')
  );
}
