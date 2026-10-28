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

import type { JsonValue } from '@/lib/json';
// The words a step card and the inspector show. Action titles come from the KMP block names
// (pipelineBlock.<type>, copied by scripts/gen-schema-words.mjs); an action without one is humanized from its
// type, as the KMP editor does. Descriptions and field help are the bot's schema words.
import type {
  PipelineActionDescriptor,
  PipelineActionField,
  PipelineBlastRadius,
  PipelineCondition,
  PipelineStep,
} from '@/api/pipelines';
import type { TextOf } from '@/composables/useLocalizedText';
import { merge } from '@/lib/array';
import { humanize } from '@/lib/text';

import type { StepLane } from './stepTree';

export type Words = {
  t: (key: string, named?: Record<string, string | number>) => string;
  te: (key: string) => boolean;
};

export type StartSource = { kind: 'command' | 'chat' | 'timer' | 'event'; name: string };

const page = 'dashboard.pipelines.workbench';

export function actionTitle(type: string, { t, te }: Words): string {
  const key = `pipelineBlock.${type}`;

  return te(key)
    ? t(key)
    : humanize(type);
}

export function stepTitle(step: PipelineStep, words: Words): string {
  return step.block_kind === null
    ? actionTitle(step.action.type, words)
    : words.t(`${page}.block.${step.block_kind}`);
}

export function stepSentence(
  descriptor: PipelineActionDescriptor | undefined,
  text: TextOf,
): string {
  return descriptor
    ? text(descriptor.description, '')
    : '';
}

export function conditionSentence(condition: PipelineCondition, { t, te }: Words): string {
  const operatorKey = `${page}.operator.${condition.operator}`;
  const named = {
    left: condition.left,
    right: condition.right,
    operator: te(operatorKey)
      ? t(operatorKey)
      : condition.operator,
  };

  return condition.negate
    ? t(`${page}.onlyIfNot`, named)
    : t(`${page}.onlyIf`, named);
}

function isEmpty(value: JsonValue | undefined): boolean {
  if (Array.isArray(value))
    return value.length === 0;

  return value === undefined || value === null || String(value).trim() === '';
}

// A field's name as the KMP editor labels it (pipelineField.<name>), humanized when it has no label.
export function fieldLabel(name: string, { t, te }: Words): string {
  const key = `pipelineField.${name}`;

  return te(key)
    ? t(key)
    : humanize(name);
}

// The required fields the step still leaves empty: the card says "Not set up yet" for them.
export function missingFields(
  step: PipelineStep,
  descriptor: PipelineActionDescriptor | undefined,
  words: Words,
): string[] {
  return (descriptor?.fields ?? [])
    .filter((field) =>
      field.required && isEmpty(step.action[field.name]))
    .map((field) =>
      fieldLabel(field.name, words));
}

export function fieldHelp(type: string, field: PipelineActionField, text: TextOf): string {
  return text(field.description, '') || text({ key: `pipeline.${type}.${field.name}.help` }, '');
}

// A saved value as one line of text; an empty value is null so the inspector can say "Not set".
export function valueText(value: JsonValue | undefined): string | null {
  if (isEmpty(value))
    return null;

  if (Array.isArray(value))
    return value.map(String).join(', ');

  if (typeof value === 'object')
    return JSON.stringify(value);

  return String(value);
}

export function laneTitle(lane: StepLane, index: number, { t }: Words): string {
  if (lane.kind === 'case') {
    return lane.head?.block_config?.is_default === true
      ? t(`${page}.lane.otherwise`)
      : t(`${page}.lane.case`, { match: valueText(lane.head?.block_config?.match) ?? '' });
  }

  if (lane.kind === 'path')
    return t(`${page}.lane.path`, { letter: String.fromCharCode(65 + index) });

  return t(`${page}.lane.${lane.kind}`);
}

function sourcesOf(kind: StartSource['kind'], names: string[]): StartSource[] {
  return names.map((name) =>
    ({ kind, name }));
}

// Everything that starts this pipeline, from its blast radius.
export function startSources(radius: PipelineBlastRadius | undefined): StartSource[] {
  if (!radius)
    return [];

  const events = radius.eventResponseEventTypes.map(humanize);
  const sources = merge(
    sourcesOf('command', radius.commandNames),
    sourcesOf('chat', radius.chatTriggerPatterns),
    sourcesOf('timer', radius.timerNames),
    sourcesOf('event', events),
  );

  return [...sources];
}
