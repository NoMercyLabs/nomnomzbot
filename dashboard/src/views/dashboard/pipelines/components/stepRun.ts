// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

function stepRun(row: PipelineTraceStep): StepRun {
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
  };
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

export function runSummary(run: PipelineTestRun): RunSummary {
  const marks = [...runMarks(run).values()];

  return {
    ran: marks.length,
    failed: marks.filter((mark) =>
      mark.state === 'failed').length,
  };
}
