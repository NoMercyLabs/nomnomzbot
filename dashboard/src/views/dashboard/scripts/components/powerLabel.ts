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

import { humanize } from '@/lib/text';

type Say = (key: string, named?: Record<string, string>) => string;
type Has = (key: string) => boolean;

const NAMES = 'dashboard.scripts.detail.powers.names';
const ACTION_PREFIX = 'actions.invoke:';

// A power key such as "chat.send" reads as words: "Send chat messages".
// An action power ("actions.invoke:play_track_once") names the action. A key without words shows its own words.
export function powerLabel(key: string, t: Say, te: Has): string {
  if (key.startsWith(ACTION_PREFIX)) {
    return t('dashboard.scripts.detail.powers.action', {
      name: humanize(key.slice(ACTION_PREFIX.length)).toLowerCase(),
    });
  }

  const path = `${NAMES}.${key}`;

  return te(path)
    ? t(path)
    : humanize(key);
}
