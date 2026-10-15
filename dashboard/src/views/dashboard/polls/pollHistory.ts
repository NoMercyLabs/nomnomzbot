// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { PollHistoryItem } from '@/api/liveOps';
import type { StatusTone } from '@/components/layout/StatusBadge.vue';

export const historyKinds = ['all', 'poll', 'prediction', 'chat-poll'] as const;

export type HistoryKind = (typeof historyKinds)[number];

// The rows the kind filter keeps; 'all' keeps every row.
export function filterByKind(
  items: readonly PollHistoryItem[],
  kind: HistoryKind,
): PollHistoryItem[] {
  if (kind === 'all')
    return [...items];

  return items.filter((item) => item.kind === kind);
}

// Still taking votes reads live, a normal end reads ok, a canceled or lost one reads off.
export function statusTone(status: string): StatusTone {
  const normalized = status.toLowerCase();

  if (normalized === 'active' || normalized === 'open' || normalized === 'locked')
    return 'live';

  if (normalized === 'completed' || normalized === 'resolved' || normalized === 'closed')
    return 'ok';

  return 'off';
}
