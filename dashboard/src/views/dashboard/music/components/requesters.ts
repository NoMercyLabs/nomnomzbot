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

import type { QueueItem } from '@/api/music';

export type Requester = { name: string; count: number };

// The people with songs in the queue now, most songs first. Names match without regard to case,
// the same way the bot removes them. A song with no requester is not listed.
export function requestersIn(rows: QueueItem[]): Requester[] {
  const counts = new Map<string, Requester>();

  for (const row of rows) {
    if (!row.requestedBy) continue;

    const key = row.requestedBy.toLowerCase();
    const known = counts.get(key);

    counts.set(key, { name: known?.name ?? row.requestedBy, count: (known?.count ?? 0) + 1 });
  }

  return [...counts.values()].sort((a: Requester, b: Requester) => b.count - a.count);
}
