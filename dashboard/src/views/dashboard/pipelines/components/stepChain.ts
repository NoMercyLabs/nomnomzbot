// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The text an earlier step already holds, so a later step can use the same words (Stoney 2026-10-20: a Read
// aloud step after Send message should not need the message typed again). Earlier means before the step in the
// order the engine walks them; the nearest step comes first.
import type { PipelineActionDescriptor, PipelineStep } from '@/api/pipelines';

import type { StepNode } from './stepTree';

export type EarlierText = {
  key: string;
  number: string;
  step: PipelineStep;
  field: string;
  text: string;
};

function walk(nodes: StepNode[]): StepNode[] {
  return nodes.flatMap((node) =>
    [node, ...walk(node.lanes.flatMap((lane) =>
      lane.nodes))]);
}

function textsOf(node: StepNode, descriptor: PipelineActionDescriptor | undefined): EarlierText[] {
  const fields = (descriptor?.fields ?? []).filter((field) =>
    field.kind === 'text' && !field.repeatable);

  return fields.flatMap((field) => {
    const value = node.step.action[field.name];

    if (typeof value !== 'string' || value.trim() === '')
      return [];

    // A field that names a variable the step writes (pick_from_list's variable) ties the steps together: the
    // later step reads what the earlier one produced, not a copy of its settings.
    return [{
      key: `${node.key}:${field.name}`,
      number: node.number,
      step: node.step,
      field: field.name,
      text: field.declaresVariable
        ? `{${value.trim()}}`
        : value,
    }];
  });
}

export function earlierTexts(
  tree: StepNode[],
  key: string,
  descriptors: Map<string, PipelineActionDescriptor>,
): EarlierText[] {
  const order = walk(tree);
  const at = order.findIndex((node) =>
    node.key === key);

  if (at < 0)
    return [];

  const found = order
    .slice(0, at)
    .filter((node) =>
      node.step.block_kind === null)
    .reverse()
    .flatMap((node) =>
      textsOf(node, descriptors.get(node.step.action.type)));

  // The same words from two steps are one choice; the nearest step names it.
  return found.filter((item, index) =>
    found.findIndex((other) =>
      other.text === item.text) === index);
}

// The variable names earlier steps write, so a later template field knows {name} is not a typo.
export function declaredVariables(
  tree: StepNode[],
  key: string,
  descriptors: Map<string, PipelineActionDescriptor>,
): string[] {
  const order = walk(tree);
  const at = order.findIndex((node) =>
    node.key === key);
  const names = order.slice(0, Math.max(at, 0)).flatMap((node) =>
    (descriptors.get(node.step.action.type)?.fields ?? [])
      .filter((field) =>
        field.declaresVariable)
      .map((field) =>
        node.step.action[field.name])
      .filter((value) =>
        typeof value === 'string' && value.trim() !== '')
      .map((value) =>
        String(value).trim()));

  return [...new Set(names)];
}
