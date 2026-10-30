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

import type { LocationQuery, LocationQueryRaw } from 'vue-router';

// What the Runs tab keeps in the address, so a copied link reopens the same list and the same open run.
export const RUNS_QUERY_KEYS = ['failuresOnly', 'pipeline', 'page', 'run'] as const;

export type RunsView = {
  failuresOnly: boolean;
  pipelineId: string;
  page: number;
  runId: number | null;
};

function first(value: LocationQuery[string] | undefined): string {
  const single = Array.isArray(value)
    ? value[0]
    : value;

  return single ?? '';
}

function positive(value: string): number | null {
  const parsed = Number(value);

  return Number.isInteger(parsed) && parsed > 0
    ? parsed
    : null;
}

export function readRunsView(query: LocationQuery) {
  return {
    failuresOnly: first(query.failuresOnly) === 'true',
    pipelineId: first(query.pipeline),
    page: positive(first(query.page)) ?? 1,
    runId: positive(first(query.run)),
  } as const satisfies RunsView;
}

// The address of the same view with some keys changed. An unset filter (false, '', page 1, no run) leaves the
// address, so the shortest address always names the default view.
export function runsQuery(query: LocationQuery, next: Partial<RunsView>): LocationQueryRaw {
  const view = { ...readRunsView(query), ...next };

  return {
    ...query,
    failuresOnly: view.failuresOnly
      ? 'true'
      : undefined,
    pipeline: view.pipelineId || undefined,
    page: view.page > 1
      ? String(view.page)
      : undefined,
    run: view.runId === null
      ? undefined
      : String(view.runId),
  };
}

// The address of another tab: the keys of the Runs tab stay behind.
export function withoutRunsKeys(query: LocationQuery): LocationQueryRaw {
  return Object.fromEntries(
    Object.entries(query).filter(([key]) =>
      !(RUNS_QUERY_KEYS as readonly string[]).includes(key)),
  );
}
