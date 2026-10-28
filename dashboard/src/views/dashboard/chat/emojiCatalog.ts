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

// The Unicode emoji with their GitHub short codes (the gemoji set the KMP composer offers), loaded on the first
// ":" so the page itself stays light. The group is the Unicode group the picker sorts them by; the tags let a
// search for "love" find the heart.
export type EmojiEntry = {
  glyph: string;
  shortcodes: string[];
  group: number;
  tags: readonly string[];
  // The five skin-tone versions, light to dark; empty when the emoji has none.
  skins: readonly string[];
};

let loading: Promise<EmojiEntry[]> | null = null;

async function readCatalog() {
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
      skins: emoji.skins?.map((skin) =>
        skin.unicode) ?? [],
    } as const satisfies EmojiEntry];
  });
}

export function loadEmoji(): Promise<EmojiEntry[]> {
  loading ??= readCatalog();

  return loading;
}
