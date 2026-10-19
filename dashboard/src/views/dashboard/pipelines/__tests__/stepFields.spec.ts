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
import { lookup } from '@/lib/array';

import { actionRows, blockRows, choiceContext, type FieldRow, itemText, listItem, settingsAfter } from '../components/stepFields';

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
    stop_on_match: false,
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

const labels = { 'pipelineField.message': 'Message' } as const satisfies Record<string, string>;
const words = {
  t: (key: string) =>
    lookup(labels, key) ?? key,
  te: (key: string) =>
    key in labels,
  text: (_text: unknown, fallback: string) =>
    fallback,
};

const brief = (rows: FieldRow[]): string[] =>
  rows.map((row) =>
    `${row.name}:${row.control}:${String(row.value)}`);

describe('the choices a picker narrows by', () => {
  it('holds the closed choices that are set, and nothing else', () => {
    const rows = actionRows(
      step({
        action: {
          type: 'playlist_add',
          provider: 'youtube',
          playlist_id: 'PL1',
          track_uri: 'x',
        },
      }),
      {
        type: 'playlist_add',
        fields: [
          field({
            name: 'provider',
            kind: 'enum',
            options: ['spotify', 'youtube'],
          }),
          field({ name: 'playlist_id', kind: 'music_playlist' }),
          field({ name: 'track_uri', kind: 'text' }),
        ],
      } as unknown as PipelineActionDescriptor,
      words,
    );

    expect(choiceContext(rows)).toEqual({ provider: 'youtube' });
  });

  it('leaves out a choice that is not set, so the bot uses its active service', () => {
    const rows = actionRows(
      step({ action: { type: 'playlist_add', playlist_id: 'PL1' } }),
      {
        type: 'playlist_add',
        fields: [field({
          name: 'provider',
          kind: 'enum',
          options: ['spotify', 'youtube'],
        })],
      } as unknown as PipelineActionDescriptor,
      words,
    );

    expect(choiceContext(rows)).toEqual({});
  });
});

describe('the settings an edit writes', () => {
  const descriptor = {
    type: 'playlist_add',
    fields: [
      field({
        name: 'provider',
        kind: 'enum',
        options: ['spotify', 'youtube'],
      }),
      field({ name: 'playlist_id', kind: 'music_playlist' }),
      field({ name: 'track_uri', kind: 'text' }),
    ],
  } as unknown as PipelineActionDescriptor;
  const rows = actionRows(
    step({
      action: {
        type: 'playlist_add',
        provider: 'spotify',
        playlist_id: 'PL1',
        track_uri: 'x',
      },
    }),
    descriptor,
    words,
  );
  const named = (name: string): FieldRow =>
    rows.find((row) =>
      row.name === name) as FieldRow;

  it('clears the saved playlist when the provider changes', () => {
    const written = settingsAfter(rows, named('provider'), 'youtube');

    expect(written).toEqual({ provider: 'youtube', playlist_id: undefined });
    expect('playlist_id' in written).toBe(true);
    expect('track_uri' in written).toBe(false);
  });

  it('keeps the playlist when the provider is set to what it already is', () => {
    expect(settingsAfter(rows, named('provider'), 'spotify')).toEqual({ provider: 'spotify' });
  });

  it('writes only the edited setting for a non-choice edit', () => {
    expect(settingsAfter(rows, named('track_uri'), 'y')).toEqual({ track_uri: 'y' });
  });
});

describe('the step form rows', () => {
  it('shows Pick from list as a picker of saved lists that keeps the saved list name', () => {
    const descriptor: PipelineActionDescriptor = {
      type: 'pick_from_list',
      category: { key: 'pipeline.category.pick_lists' },
      description: { key: 'pipeline.pick_from_list.description' },
      fields: [field({
        name: 'list',
        kind: 'pick_list',
        required: true,
      })],
    };
    const rows = actionRows(step({ action: { type: 'pick_from_list', list: 'fight_moves' } }), descriptor, words);

    expect(brief(rows)).toEqual(['list:pick:fight_moves']);
    expect(rows[0]?.kind).toBe('pick_list');
  });

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
      'args:list:undefined',
      'headers:map:undefined',
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

    expect(rows.every((row) =>
      row.target === 'action')).toBe(true);
  });

  it('saves a list item as an object only when it is a whole JSON object', () => {
    expect(listItem('{"requestType":"GetVersion"}')).toEqual({ requestType: 'GetVersion' });
    expect(listItem('{user}')).toBe('{user}');
    expect(listItem('[1, 2]')).toBe('[1, 2]');
    expect(listItem('hi')).toBe('hi');

    expect(itemText({ requestType: 'GetVersion' })).toBe('{\n  "requestType": "GetVersion"\n}');
    expect(itemText(3)).toBe('3');
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

    expect(rows.every((row) =>
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
