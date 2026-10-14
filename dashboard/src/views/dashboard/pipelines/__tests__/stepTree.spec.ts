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

import type { PipelineStep } from '@/api/pipelines';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import { buildStepTree, findNode, type StepLane, type StepNode } from '../components/stepTree';
import { laneTitle, missingFields } from '../components/stepWords';

function step(over: Partial<PipelineStep>): PipelineStep {
  return {
    id: null,
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order: 0,
    action: { type: 'send_message', message: 'hi' },
    condition: null,
    continue_on_error: false,
    ...over,
  };
}

function numbers(nodes: StepNode[]): string[] {
  return nodes.flatMap((node: StepNode) =>
    [`${node.number} ${node.step.id}`, ...node.lanes.flatMap((lane) =>
      numbers(lane.nodes))]);
}

function lanesAt(tree: StepNode[], index: number): StepLane[] {
  return tree[index]?.lanes ?? [];
}

i18n.global.mergeLocaleMessage('en', en);
const words = { t: i18n.global.t, te: i18n.global.te } as Parameters<typeof laneTitle>[2];

describe('the step tree', () => {
  it('orders steps by their order, not by where the server listed them', () => {
    const tree = buildStepTree([step({ id: 'b', order: 1 }), step({ id: 'a', order: 0 })]);

    expect(numbers(tree)).toEqual(['1 a', '2 b']);
  });

  it('gives a switch one lane per case, the default case reading Otherwise', () => {
    const tree = buildStepTree([
      step({
        id: 'sw',
        block_kind: 'switch',
        block_config: { value: '{args.1}' },
      }),
      step({
        id: 'c1',
        parent_step_id: 'sw',
        block_kind: 'switch_case',
        block_config: {
          match: 'red',
          operator: 'eq',
          is_default: false,
        },
      }),
      step({
        id: 'c2',
        parent_step_id: 'sw',
        order: 1,
        block_kind: 'switch_case',
        block_config: {
          match: '',
          operator: 'eq',
          is_default: true,
        },
      }),
      step({ id: 'say-red', parent_step_id: 'c1' }),
      step({ id: 'say-other', parent_step_id: 'c2' }),
    ]);
    const lanes = lanesAt(tree, 0);

    expect(lanes.map((lane) =>
      laneTitle(lane, 0, words))).toEqual(['When it is red', 'Otherwise']);

    expect(numbers(tree)).toEqual(['1 sw', '1.1 say-red', '1.1 say-other']);
  });

  it('names random paths A and B in their order', () => {
    const tree = buildStepTree([
      step({ id: 'rnd', block_kind: 'random_branch' }),
      step({
        id: 'p1',
        parent_step_id: 'rnd',
        block_kind: 'random_case',
        block_config: { weight: 3 },
      }),
      step({
        id: 'p2',
        parent_step_id: 'rnd',
        order: 1,
        block_kind: 'random_case',
        block_config: { weight: 1 },
      }),
    ]);

    expect(lanesAt(tree, 0).map((lane, index) =>
      laneTitle(lane, index, words))).toEqual(['Path A', 'Path B']);
  });

  it('splits a try into It works and It fails by branch, and a loop into one lane', () => {
    const tree = buildStepTree([
      step({ id: 'try', block_kind: 'try' }),
      step({
        id: 'risky',
        parent_step_id: 'try',
        branch: 'then',
      }),
      step({
        id: 'sorry',
        parent_step_id: 'try',
        branch: 'else',
      }),
      step({
        id: 'loop',
        order: 1,
        block_kind: 'loop',
        block_config: { mode: 'count', count: 3 },
      }),
      step({ id: 'tick', parent_step_id: 'loop' }),
    ]);

    expect(lanesAt(tree, 0).map((lane) =>
      lane.kind)).toEqual(['works', 'fails']);

    expect(lanesAt(tree, 1).map((lane) =>
      lane.kind)).toEqual(['each']);

    expect(numbers(tree)).toEqual(['1 try', '1.1 risky', '1.1 sorry', '2 loop', '2.1 tick']);
    expect(findNode(tree, '2:0.1')?.step.id).toBe('tick');
    expect(findNode(tree, '1:1.1')?.step.id).toBe('sorry');
  });

  it('lists only the required fields left empty as missing', () => {
    const descriptor = {
      type: 'timeout',
      category: { key: 'pipeline.category.moderation' },
      description: { key: 'pipeline.timeout.description' },
      fields: [
        {
          name: 'user_id',
          kind: 'twitch_user',
          required: true,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
        {
          name: 'duration',
          kind: 'number',
          required: true,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
        {
          name: 'reason',
          kind: 'text',
          required: false,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
      ],
    };

    const labels: Record<string, string> = { 'pipelineField.user_id': 'Viewer' };
    const words = {
      t: (key: string) =>
        labels[key] ?? key,
      te: (key: string) =>
        key in labels,
    };

    expect(missingFields(step({ action: {
      type: 'timeout',
      duration: '  ',
    } }), descriptor, words))
      .toEqual(['Viewer', 'Duration']);

    expect(missingFields(step({ action: {
      type: 'timeout',
      user_id: 'u1',
      duration: 0,
    } }), descriptor, words))
      .toEqual([]);
  });
});
