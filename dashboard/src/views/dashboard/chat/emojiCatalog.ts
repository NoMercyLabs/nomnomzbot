// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The Unicode emoji with their GitHub short codes (the gemoji set the KMP composer offers), loaded on the first
// ":" so the page itself stays light. The group is the Unicode group the picker sorts them by; the tags let a
// search for "love" find the heart.
export type EmojiEntry = {
  glyph: string;
  shortcodes: string[];
  group: number;
  tags: readonly string[];
};

let loading: Promise<EmojiEntry[]> | null = null;

async function readCatalog(): Promise<EmojiEntry[]> {
  const [compact, github] = await Promise.all([
    import('emojibase-data/en/compact.json'),
    import('emojibase-data/en/shortcodes/github.json'),
  ]);
  const codes = github.default;

  return compact.default.flatMap((emoji) => {
    const found = codes[emoji.hexcode];

    if (!found)
      return [];

    return [{
      glyph: emoji.unicode,
      shortcodes: Array.isArray(found)
        ? found
        : [found],
      group: emoji.group ?? -1,
      tags: emoji.tags ?? [],
    }];
  });
}

export function loadEmoji(): Promise<EmojiEntry[]> {
  loading ??= readCatalog();

  return loading;
}
