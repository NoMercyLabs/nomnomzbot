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

// A pipeline definition's payload is the graph the server fans out to every channel copy: `{ "steps": [...] }`
// in the same wire shape the channel pipeline editor edits and saves. Reading it for the editor and writing it
// back change no part of it, so the step editor can stand in for the raw JSON.
import { parseGraph, type PipelineGraph, type PipelineStep } from '@/api/pipelines';
import type { JsonObject, JsonValue } from '@/lib/json';

const STEP_KEYS: readonly string[] = [
  'id',
  'parent_step_id',
  'branch',
  'block_kind',
  'block_config',
  'order',
  'action',
  'condition',
  'continue_on_error',
  'stop_on_match',
] as const satisfies (keyof PipelineStep)[];

function isRecord(value: JsonValue | undefined): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function parseJson(payloadJson: string): JsonValue | undefined {
  try {
    return JSON.parse(payloadJson) as JsonValue;
  } catch {
    return undefined;
  }
}

function holdsOnlyStepKeys(step: JsonValue): boolean {
  return isRecord(step) && Object.keys(step).every((name) => STEP_KEYS.includes(name));
}

// The steps of the payload, or null when the payload holds anything the step editor would drop: another top-level
// field, a step field it does not know, or text that is not JSON. The caller then shows the raw JSON.
export function readPipelinePayload(payloadJson: string): PipelineGraph | null {
  const parsed = parseJson(payloadJson);

  if (!isRecord(parsed))
    return null;

  const names = Object.keys(parsed);
  const steps = parsed.steps;

  if (names.length !== 1 || names[0] !== 'steps' || !Array.isArray(steps))
    return null;

  if (!steps.every(holdsOnlyStepKeys))
    return null;

  return parseGraph(parsed);
}

export function writePipelinePayload(steps: PipelineStep[]): string {
  return JSON.stringify({ steps });
}
