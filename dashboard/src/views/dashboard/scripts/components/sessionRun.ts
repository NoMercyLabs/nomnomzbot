// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { TestRunResult } from '@/composables/useEditorBridge';

// One test run made on this page since it opened. The bot keeps no run table, so this list lives in memory only.
export interface SessionRun {
  id: number;
  scriptId: string;
  scriptName: string;
  at: Date;
  // The sample the run started from, or null when it ran with no sample.
  trigger: string | null;
  result: TestRunResult;
}

export type RunFilter = 'all' | 'failed' | 'tests';

export function matchesRunFilter(run: SessionRun, filter: RunFilter): boolean {
  if (filter === 'failed')
    return !run.result.success;

  if (filter === 'tests')
    return run.trigger !== null;

  return true;
}
