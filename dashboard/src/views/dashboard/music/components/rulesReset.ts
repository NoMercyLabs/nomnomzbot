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

import { rulesOf, type MusicConfig, type MusicConfigChange } from '@/api/music';

type Rule = MusicConfigChange[keyof MusicConfigChange];

const same = (left: Rule, right: Rule): boolean =>
  JSON.stringify(left ?? null) === JSON.stringify(right ?? null);

// The bot stores no per-stream cap as zero, so a default of no cap is written as zero.
function written(key: string, value: Rule): Rule {
  return key === 'perStreamLimit' && value == null
    ? 0
    : value;
}

// Only the rules that differ from the platform defaults are written back, like Save sends only what changed.
export function resetChange(config: MusicConfig, defaults: MusicConfig): MusicConfigChange {
  const perStream = config.perStreamLimit || null;
  const current = new Map(Object.entries({ ...config, perStreamLimit: perStream }));
  const differing = Object.entries(rulesOf(defaults))
    .filter(([key, value]): boolean => !same(value, current.get(key)));
  const changes = differing.map(([key, value]) => [key, written(key, value)] as const);

  return Object.fromEntries(changes) as MusicConfigChange;
}
