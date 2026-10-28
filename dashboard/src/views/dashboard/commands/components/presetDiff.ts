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

import type { CommandListItem, CommandPreset } from '@/api/commands';

// The fields a reset writes back, in the order the confirm lists them.
export const presetFields = [
  'description',
  'templateResponse',
  'templateResponses',
  'minPermissionLevel',
  'cooldownSeconds',
  'userCooldownSeconds',
  'aliases',
  'prefixMode',
  'matchMode',
] as const;

export type PresetField = (typeof presetFields)[number];
export type PresetFields = Pick<CommandPreset, PresetField>;

const same = (left: readonly string[], right: readonly string[]): boolean =>
  left.length === right.length && left.every((line, index): boolean => line === right[index]);

function matches(field: PresetField, row: CommandListItem, preset: PresetFields): boolean {
  switch (field) {
    case 'templateResponses':
      return same(row.templateResponses ?? [], preset.templateResponses);
    case 'aliases':
      return same(row.aliases, preset.aliases);
    case 'cooldownSeconds':
    case 'userCooldownSeconds':
      return Number(row[field]) === Number(preset[field]);
    case 'minPermissionLevel':
      return row.minPermissionLevel.toLowerCase() === preset.minPermissionLevel.toLowerCase();
    default:
      return (row[field] ?? '') === (preset[field] ?? '');
  }
}

// The fields on which the command differs from its preset: what a reset would overwrite.
export function presetChanges(row: CommandListItem, preset: PresetFields): PresetField[] {
  return presetFields.filter((field): boolean => !matches(field, row, preset));
}
