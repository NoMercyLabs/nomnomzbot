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

// What a test run did, per step on the canvas: the bot traces every step it ran by the step's id, so a step
// without a row did not run (a check sent the run down another lane, or an earlier step stopped it).
import type { PipelineTestRun, PipelineTraceStep } from '@/api/pipelines';

export type StepRunState = 'done' | 'failed' | 'skipped';

export type VariableChange = NonNullable<PipelineTraceStep['variableChanges']>[number];

export type StepRun = {
  state: StepRunState;
  output: string | null;
  error: string | null;
  // The lane a check took (then, else, a case) and how often a repeat ran.
  branch: string | null;
  iterations: number | null;
  changes: VariableChange[];
};

export type RunMarks = ReadonlyMap<string, StepRun>;

const skipped: StepRun = {
  state: 'skipped',
  output: null,
  error: null,
  branch: null,
  iterations: null,
  changes: [],
};

function stepRun(row: PipelineTraceStep) {
  return {
    state: row.error
      ? 'failed'
      : 'done',
    output: row.output ?? null,
    error: row.error ?? null,
    branch: row.branch ?? null,
    iterations: row.iterations === null || row.iterations === undefined
      ? null
      : Number(row.iterations),
    changes: row.variableChanges ?? [],
  } as const satisfies StepRun;
}

// A step inside a repeat runs once per round; its last round is what the canvas shows.
export function runMarks(run: PipelineTestRun): RunMarks {
  return new Map((run.trace ?? []).map((row) =>
    [row.stepId, stepRun(row)]));
}

export function runOf(marks: RunMarks, stepId: string | null): StepRun {
  return (stepId === null
    ? undefined
    : marks.get(stepId)) ?? skipped;
}

export type RunSummary = { ran: number; failed: number };

export function runSummary(run: PipelineTestRun) {
  const marks = [...runMarks(run).values()];

  return {
    ran: marks.length,
    failed: marks.filter((mark) =>
      mark.state === 'failed').length,
  } as const satisfies RunSummary;
}
