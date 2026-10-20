// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
