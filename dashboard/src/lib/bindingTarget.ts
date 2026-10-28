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

import type { components } from '@/api/schema';
import type { JsonValue } from '@/lib/json';

// A binding target is what a timer, trigger, reward or giveaway runs: a command, an action or a pipeline.
// The API sends the kind as an integer (0 command, 1 pipeline, 2 action). The dashboard works with a name.
export type TargetKind = 'command' | 'pipeline' | 'action';

// An action target carries the settings the action runs with.
export type ActionParams = Record<string, JsonValue | undefined>;

// The generated schema types the params as an open object, so the wire accepts that shape.
// The server stores them as JSON, so reading them as ActionParams is one narrowing, done in targetFromWire.
type WireParams = NonNullable<components['schemas']['AutomationInvokeRequest']['params']>;

export type PickedTarget = {
  kind: TargetKind;
  id: string;
  actionType?: string;
  params?: ActionParams;
};

export type WireTarget = {
  kind: number | string;
  id?: string | null;
  actionType?: string | null;
  params?: WireParams | null;
};

const KIND_BY_NUMBER = ['command', 'pipeline', 'action'] as const satisfies readonly TargetKind[];

export function targetKindToWire(kind: TargetKind): number {
  return KIND_BY_NUMBER.indexOf(kind);
}

function targetKindFromWire(kind: number | string): TargetKind | null {
  if (typeof kind === 'number')
    return KIND_BY_NUMBER[kind] ?? null;

  const name = kind.toLowerCase();

  return KIND_BY_NUMBER.find((known) => known === name) ?? null;
}

// Nothing bound: the shape every surface starts from.
export const EMPTY_TARGET = null;

export function targetFromWire(wire: WireTarget | null | undefined): PickedTarget | null {
  if (!wire)
    return null;

  const kind = targetKindFromWire(wire.kind);

  if (kind === null)
    return null;

  // The server clears a binding with a pipeline that has no id, so an id-less pipeline is nothing bound.
  if (kind === 'pipeline' && !wire.id)
    return null;

  return {
    kind,
    id: wire.id ?? '',
    ...(wire.actionType
      ? { actionType: wire.actionType }
      : {}),
    ...(wire.params
      ? { params: wire.params as ActionParams }
      : {}),
  };
}

// A null target is sent as an empty pipeline: that is how the server clears a binding.
// Leaving the field out would leave the row unchanged.
// The server reads the kind as an integer only; the name form is accepted on the way in, never sent.
export function targetToWire(target: PickedTarget | null): WireTarget & { kind: number } {
  if (target === null)
    return { kind: targetKindToWire('pipeline'), id: '' };

  return {
    kind: targetKindToWire(target.kind),
    id: target.id,
    ...(target.actionType
      ? { actionType: target.actionType }
      : {}),
    ...(target.params
      ? { params: target.params }
      : {}),
  };
}

// The text a read-only view shows for a target: the name when the lists know it, else the raw id.
export function targetLabel(
  target: PickedTarget | null,
  names: { commands: ReadonlyMap<string, string>; pipelines: ReadonlyMap<string, string> },
): string {
  if (target === null)
    return '';

  if (target.kind === 'action')
    return target.actionType ?? '';

  if (target.kind === 'command')
    return `!${names.commands.get(target.id) ?? target.id}`;

  return names.pipelines.get(target.id) ?? target.id;
}

// What a row is bound to: its target when the server sent one, else the pipeline id older rows carry.
export function boundTarget(
  wire: WireTarget | null | undefined,
  pipelineId: string | null | undefined,
): PickedTarget | null {
  const fromWire = targetFromWire(wire);

  if (fromWire !== null)
    return fromWire;

  return pipelineId
    ? { kind: 'pipeline', id: pipelineId }
    : null;
}

export function sameTarget(left: PickedTarget | null, right: PickedTarget | null): boolean {
  if (left === null || right === null)
    return left === right;

  return left.kind === right.kind &&
    left.id === right.id &&
    left.actionType === right.actionType &&
    JSON.stringify(left.params ?? {}) === JSON.stringify(right.params ?? {});
}
