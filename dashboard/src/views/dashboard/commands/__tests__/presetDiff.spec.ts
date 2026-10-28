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

import { describe, expect, it } from 'vitest';

import type { CommandListItem } from '@/api/commands';

import { presetChanges, type PresetFields } from '../components/presetDiff';

const preset = {
  key: 'hug',
  tier: 'template',
  description: 'Hug someone',
  templateResponse: '{{user}} hugs {{target}}',
  templateResponses: [],
  minPermissionLevel: 'Everyone',
  cooldownSeconds: 5,
  userCooldownSeconds: 10,
  aliases: ['cuddle'],
  prefixMode: 'Default',
  matchMode: 'StartsWith',
} as const satisfies PresetFields & { key: string; tier: string };

const row = (overrides: Partial<CommandListItem> = {}): CommandListItem =>
  ({
    name: 'hug',
    description: 'Hug someone',
    templateResponse: '{{user}} hugs {{target}}',
    templateResponses: null,
    minPermissionLevel: 'Everyone',
    cooldownSeconds: 5,
    userCooldownSeconds: 10,
    aliases: ['cuddle'],
    prefixMode: 'Default',
    matchMode: 'StartsWith',
    ...overrides,
  }) as CommandListItem;

describe('presetChanges', () => {
  it('lists nothing when the command already matches its preset', () => {
    expect(presetChanges(row(), preset)).toEqual([]);
  });

  it('lists exactly the fields that differ, in a fixed order', () => {
    const changed = row({
      templateResponse: 'custom',
      cooldownSeconds: 30,
      minPermissionLevel: 'Moderator',
    });

    expect(presetChanges(changed, preset)).toEqual([
      'templateResponse',
      'minPermissionLevel',
      'cooldownSeconds',
    ]);
  });

  it('compares lists by content and treats a missing list as empty', () => {
    expect(presetChanges(row({ aliases: ['cuddle'], templateResponses: [] }), preset)).toEqual([]);
    expect(presetChanges(row({ aliases: ['cuddle', 'squeeze'] }), preset)).toEqual(['aliases']);
    expect(presetChanges(row({ templateResponses: ['a'] }), preset)).toEqual(['templateResponses']);
  });

  it('compares numbers sent as text and permission levels in any letter case', () => {
    expect(
      presetChanges(row({ cooldownSeconds: '5', minPermissionLevel: 'everyone' }), preset),
    ).toEqual([]);
  });
});
