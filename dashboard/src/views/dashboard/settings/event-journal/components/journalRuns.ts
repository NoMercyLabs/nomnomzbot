// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
