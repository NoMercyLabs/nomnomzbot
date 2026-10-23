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
  const own = `common.blast.categories.${key}`;

  if (te(key))
    return t(key);

  return te(own)
    ? t(own)
    : (key.split('.').pop() ?? key);
}

// The line of one kind: "2 pipelines: Raid flow, Hype", or "5 saved versions" when the bot names none.
function lineKey(isMinimum: boolean, named: boolean): string {
  const kind: string = named
    ? 'line'
    : 'count';

  return isMinimum
    ? `common.blast.${kind}AtLeast`
    : `common.blast.${kind}`;
}

// What else uses a script, one line per kind. Empty when nothing does.
export function blastLines(blast: BlastRadius, t: Say, te: Has): string[] {
  return blast.categories
    .filter((category) => Number(category.count) > 0)
    .map((category) =>
      t(lineKey(blast.isMinimum, category.sample.length > 0), {
        count: Number(category.count),
        what: categoryLabel(category.categoryKey, t, te),
        names: category.sample.join(', '),
      }),
    );
}
