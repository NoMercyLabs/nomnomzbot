// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';

import type { PipelineActionDescriptor, PipelineActionField, PipelineStep } from '@/api/pipelines';

import { actionRows, blockRows, type FieldRow } from '../components/stepFields';

function step(over: Partial<PipelineStep>): PipelineStep {
  return {
    id: 's1',
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order: 0,
    action: { type: 'block' },
    condition: null,
    continue_on_error: false,
    ...over,
  };
}

function field(over: Partial<PipelineActionField>): PipelineActionField {
  return {
    name: 'message',
    kind: 'text',
    required: false,
    repeatable: false,
    description: null,
    templated: false,
    declaresVariable: false,
    ...over,
  };
}

const labels: Record<string, string> = { 'pipelineField.message': 'Message' };
const words = {
  t: (key: string) =>
    labels[key] ?? key,
  te: (key: string) =>
    key in labels,
  text: (_text: unknown, fallback: string) =>
    fallback,
};

const brief = (rows: FieldRow[]): string[] =>
  rows.map((row: FieldRow) =>
    `${row.name}:${row.control}:${String(row.value)}`);

describe('the step form rows', () => {
  it('gives each action field the control its kind needs, with its saved value', () => {
    const descriptor: PipelineActionDescriptor = {
      type: 'announce',
      category: { key: 'pipeline.category.chat' },
      description: { key: 'pipeline.announce.description' },
      fields: [
        field({ templated: true }),
        field({ name: 'reason' }),
        field({ name: 'seconds', kind: 'number' }),
        field({ name: 'quiet', kind: 'boolean' }),
        field({
          name: 'color',
          kind: 'enum',
          options: ['blue', 'dark_green'],
        }),
        field({ name: 'args', repeatable: true }),
        field({ name: 'headers', kind: 'key_value_map' }),
        field({ name: 'user_id', kind: 'twitch_user' }),
        field({ name: 'voice', kind: 'voice' }),
        field({ name: 'code_script_id', kind: 'resource_id' }),
      ],
    };
    const rows = actionRows(step({ action: {
      type: 'announce',
      message: 'hi',
      seconds: 3,
      color: 'blue',
    } }), descriptor, words);

    expect(brief(rows)).toEqual([
      'message:lines:hi',
      'reason:line:undefined',
      'seconds:number:3',
      'quiet:switch:false',
      'color:choice:blue',
      'args:fixed:undefined',
      'headers:fixed:undefined',
      'user_id:pick:undefined',
      'voice:pick:undefined',
      'code_script_id:line:undefined',
    ]);

    expect(rows[7]?.kind).toBe('twitch_user');
    expect(rows[0]?.label).toBe('Message');
    expect(rows[1]?.label).toBe('Reason');

    expect(rows[4]?.choices).toEqual([
      { value: 'blue', label: 'Blue' },
      { value: 'dark_green', label: 'Dark green' },
    ]);

    expect(rows.every((row: FieldRow) =>
      row.target === 'action')).toBe(true);
  });

  it('lets a case pick how it compares, from the comparisons the engine runs', () => {
    const rows = blockRows(step({
      block_kind: 'switch_case',
      block_config: {
        match: 'raid',
        operator: 'eq',
        is_default: false,
      },
    }), words);

    expect(brief(rows)).toEqual(['match:line:raid', 'operator:choice:eq', 'is_default:switch:false']);

    expect(rows[1]?.choices.map((choice) =>
      choice.value)).toEqual(['eq', 'ne', 'gt', 'lt', 'gte', 'lte', 'contains']);

    expect(rows.every((row: FieldRow) =>
      row.target === 'block_config')).toBe(true);
  });

  it('shows a repeat the settings of its mode only', () => {
    const repeat = blockRows(step({ block_kind: 'loop', block_config: { mode: 'repeat', count: 2 } }), words);
    const each = blockRows(step({ block_kind: 'loop', block_config: { mode: 'foreach' } }), words);

    expect(brief(repeat)).toEqual([
      'mode:choice:repeat',
      'count:number:2',
      'max_iterations:number:undefined',
      'max_loop_runtime_seconds:number:undefined',
    ]);

    expect(brief(each).slice(0, 2)).toEqual(['mode:choice:foreach', 'list_var:line:undefined']);
  });

  it('gives a switch its value, a path its weight, and a check none', () => {
    const choose = step({ block_kind: 'switch', block_config: { value: '{user}' } });
    const path = step({ block_kind: 'random_case', block_config: { weight: 1 } });

    expect(brief(blockRows(choose, words))).toEqual(['value:lines:{user}']);
    expect(brief(blockRows(path, words))).toEqual(['weight:number:1']);

    expect(blockRows(step({ block_kind: 'if' }), words)).toEqual([]);
  });
});
