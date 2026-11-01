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

import type { ActivityEvent } from '@/api/home';

export interface StreamSplit {
  newest: ActivityEvent[];
  earlier: ActivityEvent[];
}

// The two-stream window holds the newest stream's events and the one before it. The newest stream's own
// events are known (the home feed), so the earlier stream is whatever else the window holds.
export function splitByStream(newest: ActivityEvent[], window: ActivityEvent[]) {
  const inNewest = new Set<string>(newest.map((event) => event.id));

  return {
    newest,
    earlier: window.filter((event) => !inNewest.has(event.id)),
  } as const satisfies StreamSplit;
}
