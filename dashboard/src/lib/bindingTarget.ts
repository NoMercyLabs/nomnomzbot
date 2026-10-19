// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// A binding target is what a timer, trigger, reward or giveaway runs: a command, an action or a pipeline.
// The API sends the kind as an integer (0 command, 1 pipeline, 2 action). The dashboard works with a name.
export type TargetKind = 'command' | 'pipeline' | 'action';

export type PickedTarget = {
  kind: TargetKind;
  id: string;
  actionType?: string;
};

export type WireTarget = {
  kind: number | string;
  id?: string | null;
  actionType?: string | null;
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
  };
}

// A null target is sent as an empty pipeline: that is how the server clears a binding.
// Leaving the field out would leave the row unchanged.
export function targetToWire(target: PickedTarget | null): WireTarget {
  if (target === null)
    return { kind: targetKindToWire('pipeline'), id: '' };

  return {
    kind: targetKindToWire(target.kind),
    id: target.id,
    ...(target.actionType
      ? { actionType: target.actionType }
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
