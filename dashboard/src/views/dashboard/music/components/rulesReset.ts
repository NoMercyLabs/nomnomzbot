// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
