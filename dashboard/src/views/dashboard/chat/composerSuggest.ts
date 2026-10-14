// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ChatEmoteEntry } from '@/api/chat';
import { emoteFrames, smallestUrl } from '@/lib/assetCards';

import type { EmojiEntry } from './emojiCatalog';

export type SuggestionKind = 'emote' | 'emoji' | 'chatter';

export type Suggestion = {
  kind: SuggestionKind;
  label: string;
  insert: string;
  image: string | null;
  provider: string | null;
  // An emoji's five skin-tone versions, light to dark; empty for anything without them.
  skins: readonly string[];
};

// The word being typed at the caret: ":" + 2 letters asks for emotes and emoji, "@" + 1 letter for a chatter.
export type SuggestToken = {
  trigger: ':' | '@';
  query: string;
  start: number;
};

export const suggestionLimit = 12;

const tokenPattern = /(?:^|\s)([:@])([\p{L}\p{N}_]*)$/u;
const minimumQuery = { ':': 2, '@': 1 } as const satisfies Record<SuggestToken['trigger'], number>;

export function tokenAt(text: string, caret: number): SuggestToken | null {
  const match = tokenPattern.exec(text.slice(0, caret));
  const trigger = match?.[1] as SuggestToken['trigger'] | undefined;
  const query = match?.[2] ?? '';

  if (!trigger || query.length < minimumQuery[trigger])
    return null;

  return {
    trigger,
    query,
    start: caret - query.length - 1,
  };
}

// As the Twitch and 7TV composers rank: a match at the start first, then the shorter word, then A to Z.
function rankBy(query: string): (a: string, b: string) => number {
  const lower = query.toLowerCase();
  const starts = (word: string): number =>
    Number(word.toLowerCase().startsWith(lower));

  return (a, b) =>
    starts(b) - starts(a) || a.length - b.length || a.toLowerCase().localeCompare(b.toLowerCase());
}

function holds(word: string, query: string): boolean {
  return word.toLowerCase().includes(query.toLowerCase());
}

export function emoteSuggestion(emote: ChatEmoteEntry): Suggestion {
  const frames = emoteFrames(emote);

  return {
    kind: 'emote',
    label: emote.code,
    insert: emote.code,
    image: frames['2'] ?? smallestUrl(frames) ?? null,
    provider: emote.provider,
    skins: [],
  };
}

export function emojiSuggestion(entry: EmojiEntry, code = entry.shortcodes[0]): Suggestion {
  return {
    kind: 'emoji',
    label: `:${code}:`,
    insert: entry.glyph,
    image: null,
    provider: null,
    skins: entry.skins,
  };
}

export function emoteSuggestions(
  emotes: readonly ChatEmoteEntry[],
  query: string,
  limit = suggestionLimit,
): Suggestion[] {
  const rank = rankBy(query);

  return emotes
    .filter((emote) =>
      holds(emote.code, query))
    .sort((a, b) =>
      rank(a.code, b.code))
    .slice(0, limit)
    .map(emoteSuggestion);
}

// A short code that holds the query wins; a tag that holds it ("love" for the heart) shows the first short code.
function emojiCode(
  entry: EmojiEntry,
  query: string,
  rank: (a: string, b: string) => number,
): string | undefined {
  const code = entry.shortcodes.filter((shortcode) =>
    holds(shortcode, query)).sort(rank)[0];

  if (code)
    return code;

  return entry.tags.some((tag) =>
    holds(tag, query))
    ? entry.shortcodes[0]
    : undefined;
}

export function emojiSuggestions(
  emoji: readonly EmojiEntry[],
  query: string,
  limit = suggestionLimit,
): Suggestion[] {
  const rank = rankBy(query);
  const hits = emoji.flatMap((entry) => {
    const code = emojiCode(entry, query, rank);

    return code
      ? [{ entry, code }]
      : [];
  });

  return hits
    .sort((a, b) =>
      rank(a.code, b.code))
    .slice(0, limit)
    .map(({ entry, code }) =>
      emojiSuggestion(entry, code));
}

export function chatterSuggestions(names: readonly string[], query: string): Suggestion[] {
  return names
    .filter((name) =>
      holds(name, query))
    .sort(rankBy(query))
    .slice(0, suggestionLimit)
    .map((name) => ({
      kind: 'chatter',
      label: name,
      insert: `@${name}`,
      image: null,
      provider: null,
      skins: [],
    }));
}
