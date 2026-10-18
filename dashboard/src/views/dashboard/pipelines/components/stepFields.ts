// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { JsonValue } from '@/lib/json';
// The step form in the inspector: one row per setting, with the control its kind needs. Action settings come
// from the bot's catalog; block settings mirror the engine's block configs (PipelineTreeTypes.cs).
import {
  PICKER_KINDS,
  type PickerKind,
  type PipelineActionDescriptor,
  type PipelineActionField,
  type PipelineStep,
} from '@/api/pipelines';
import type { TextOf } from '@/composables/useLocalizedText';
import { humanize } from '@/lib/text';

import { fieldHelp, fieldLabel, type Words } from './stepWords';

// line and lines are text, choice is a fixed list, pick searches what the bot lists for the kind, list is a
// setting with many items, map is names with values; fixed shows the value and is not edited here.
export type FieldControl =
  | 'line' |
  'lines' |
  'number' |
  'switch' |
  'choice' |
  'pick' |
  'list' |
  'map' |
  'fixed';

export type FieldChoice = { value: string; label: string };

export type FieldRow = {
  name: string;
  label: string;
  // The bot's field kind; a pick asks the bot for this kind's options.
  kind: string;
  control: FieldControl;
  value: JsonValue | undefined;
  help: string;
  choices: FieldChoice[];
  required: boolean;
  templated: boolean;
  // Where the value lives: an action setting, a block setting, or a setting of the step itself.
  target: 'action' | 'block_config' | 'step';
};

const page = 'dashboard.pipelines.workbench';

// The comparisons a switch case runs (PipelineEngine MatchesCase).
const CASE_OPERATORS = ['eq', 'ne', 'gt', 'lt', 'gte', 'lte', 'contains'] as const satisfies string[];
const LOOP_MODES = ['repeat', 'foreach', 'while'] as const satisfies string[];

export function isPickerKind(kind: string): kind is PickerKind {
  return (PICKER_KINDS as readonly string[]).includes(kind);
}

function controlOf(field: PipelineActionField): FieldControl {
  if (field.kind === 'key_value_map')
    return 'map';

  if (field.repeatable)
    return 'list';

  if (field.kind === 'boolean')
    return 'switch';

  if (field.kind === 'number')
    return 'number';

  if (field.kind === 'enum')
    return 'choice';

  if (isPickerKind(field.kind))
    return 'pick';

  return field.templated
    ? 'lines'
    : 'line';
}

// A list item is text, unless it is a whole JSON object (an OBS request is one): then it saves as that object.
// Text with braces such as {user} is not valid JSON, so it stays text.
export function listItem(text: string): JsonValue {
  if (!text.trim().startsWith('{'))
    return text;

  try {
    const parsed: JsonValue = JSON.parse(text);

    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? parsed
      : text;
  } catch {
    return text;
  }
}

// A setting's value as text to compare, with no value as its own word.
export function jsonOf(value: JsonValue | undefined): string {
  return JSON.stringify(value) ?? 'none';
}

export function itemText(item: JsonValue | undefined): string {
  return typeof item === 'object' && item !== null
    ? JSON.stringify(item, null, 2)
    : String(item ?? '');
}

// The step's closed choices that are set (provider = youtube): what a picker's list can narrow by.
export function choiceContext(rows: FieldRow[]): Record<string, string> {
  const set = rows.filter((row: FieldRow) =>
    row.control === 'choice' && typeof row.value === 'string' && row.value !== '');

  return Object.fromEntries(set.map((row: FieldRow) =>
    [row.name, String(row.value)]));
}

// The settings one edit writes. A list the bot narrows by a closed choice (the playlist of one service) is
// cleared when that choice changes, so the step never keeps a pick the new choice does not list.
export function settingsAfter(
  rows: FieldRow[],
  edited: FieldRow,
  value: JsonValue | undefined,
): Record<string, JsonValue | undefined> {
  const written: Record<string, JsonValue | undefined> = { [edited.name]: value };

  if (edited.control !== 'choice' || value === edited.value)
    return written;

  for (const row of rows) {
    if (row.control === 'pick' && row.target === edited.target && row.value !== undefined) {
      written[row.name] = undefined;
    }
  }

  return written;
}

export function actionRows(
  step: PipelineStep,
  descriptor: PipelineActionDescriptor | undefined,
  words: Words & { text: TextOf },
): FieldRow[] {
  return (descriptor?.fields ?? []).map((field: PipelineActionField) => {
    const control = controlOf(field);
    const saved = step.action[field.name];

    return {
      name: field.name,
      label: fieldLabel(field.name, words),
      kind: field.kind,
      control,
      value: control === 'switch'
        ? saved === true
        : saved,
      help: fieldHelp(step.action.type, field, words.text),
      choices: (field.options ?? []).map((option: string) =>
        ({ value: option, label: humanize(option) })),
      required: field.required,
      templated: field.templated,
      target: 'action',
    };
  });
}

type BlockField = {
  name: string;
  control: FieldControl;
  choices?: readonly string[];
  fallback?: JsonValue | undefined;
};

function loopFields(mode: string): BlockField[] {
  const measure: BlockField[] = mode === 'foreach'
    ? [{ name: 'list_var', control: 'line' }]
    : [];
  const count: BlockField[] = mode === 'repeat'
    ? [{ name: 'count', control: 'number' }]
    : [];

  return [
    {
      name: 'mode',
      control: 'choice',
      choices: LOOP_MODES,
      fallback: 'repeat',
    },
    ...count,
    ...measure,
    { name: 'max_iterations', control: 'number' },
    { name: 'max_loop_runtime_seconds', control: 'number' },
  ];
}

function blockFields(step: PipelineStep): BlockField[] {
  const config = step.block_config ?? {};

  switch (step.block_kind) {
    case 'switch':
      return [{ name: 'value', control: 'lines' }];
    case 'switch_case':
      return [
        { name: 'match', control: 'line' },
        {
          name: 'operator',
          control: 'choice',
          choices: CASE_OPERATORS,
          fallback: 'eq',
        },
        {
          name: 'is_default',
          control: 'switch',
          fallback: false,
        },
      ];
    case 'loop':
      return loopFields(String(config.mode ?? 'repeat'));
    case 'random_case':
      return [{ name: 'weight', control: 'number' }];
    default:
      return [];
  }
}

// The words of one choice: a case compares like a condition, a repeat names its mode.
function choiceLabel(name: string, value: string, { t }: Words): string {
  return name === 'operator'
    ? t(`${page}.operator.${value}`)
    : t(`${page}.blockField.${name}.${value}`);
}

export function blockRows(step: PipelineStep, words: Words): FieldRow[] {
  const config = step.block_config ?? {};

  return blockFields(step).map((field: BlockField) =>
    ({
      name: field.name,
      label: words.t(`${page}.blockField.${field.name}.label`),
      kind: 'block',
      control: field.control,
      value: config[field.name] ?? field.fallback,
      help: words.t(`${page}.blockField.${field.name}.help`),
      choices: (field.choices ?? []).map((value: string) =>
        ({ value, label: choiceLabel(field.name, value, words) })),
      required: false,
      templated: field.name === 'value',
      target: 'block_config',
    }));
}
