// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { BlastRadius } from '@/api/codeScripts';

type Say = (key: string, named?: Record<string, string | number>) => string;
type Has = (key: string) => boolean;

// A category key is a resource key; a key this page has no words for shows its last part.
function categoryLabel(key: string, t: Say, te: Has): string {
  return te(key)
    ? t(key)
    : (key.split('.').pop() ?? key);
}

// What else uses a script, one line per kind: "2 pipelines: Raid flow, Hype". Empty when nothing does.
export function blastLines(blast: BlastRadius, t: Say, te: Has): string[] {
  return blast.categories
    .filter((category) => Number(category.count) > 0)
    .map((category) =>
      t(
        blast.isMinimum
          ? 'dashboard.scripts.delete.lineAtLeast'
          : 'dashboard.scripts.delete.line',
        {
          count: Number(category.count),
          what: categoryLabel(category.categoryKey, t, te),
          names: category.sample.join(', '),
        },
      ),
    );
}
