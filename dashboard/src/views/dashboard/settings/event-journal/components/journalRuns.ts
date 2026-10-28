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

import type { JournalEntry } from '@/api/eventJournal';

// One line of the list: the newest event of a run of the same event, and how many events the run holds.
export type JournalRun = { entry: JournalEntry; count: number };

const sameEvent = (a: JournalEntry, b: JournalEntry): boolean =>
  a.eventType === b.eventType &&
  a.source === b.source &&
  (a.actorUserId ?? null) === (b.actorUserId ?? null);

// Events in a row that are the same kind, from the same place and the same person fold into one line,
// so ten identical rows read as one line that says "10 times".
export function foldRuns(entries: readonly JournalEntry[]): JournalRun[] {
  const runs: JournalRun[] = [];

  for (const entry of entries) {
    const last = runs.at(-1);

    if (last && sameEvent(last.entry, entry)) {
      last.count += 1;
      continue;
    }

    runs.push({ entry, count: 1 });
  }

  return runs;
}
