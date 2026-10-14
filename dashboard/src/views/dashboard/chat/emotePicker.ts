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
import { merge } from '@/lib/array';

import {
  emojiSuggestion,
  emojiSuggestions,
  emoteSuggestion,
  emoteSuggestions,
  type Suggestion,
} from './composerSuggest';
import type { EmojiEntry } from './emojiCatalog';

// The picker as Twitch and FrankerFaceZ lay it out: recent picks, then the emotes per source, then the emoji
// per Unicode group. A search turns it into one list of every match.
export type PickerSection = { id: string; items: Suggestion[] };

export const pickerSearchLimit = 120;
export const recentLimit = 24;

const providerOrder = ['Twitch', 'SevenTv', 'Bttv', 'Ffz'] as const satisfies readonly string[];

// The Unicode emoji groups in their order. Group 2 holds skin tones and hair parts, which are no emoji on their own.
const emojiGroups = [
  'smileys', 'people', 'component', 'animals', 'food', 'travel', 'activities', 'objects', 'symbols', 'flags',
] as const satisfies readonly string[];
const componentGroup = 2;

function emoteSections(emotes: readonly ChatEmoteEntry[]): PickerSection[] {
  return providerOrder.flatMap((provider) => {
    const items = emotes.filter((emote) =>
      emote.provider === provider).map(emoteSuggestion);

    return items.length > 0
      ? [{ id: provider, items }]
      : [];
  });
}

function emojiSections(emoji: readonly EmojiEntry[]): PickerSection[] {
  const groups = new Map<number, Suggestion[]>();

  for (const entry of emoji) {
    if (entry.group === componentGroup || !emojiGroups[entry.group]) continue;

    const items = groups.get(entry.group) ?? [];

    items.push(emojiSuggestion(entry));
    groups.set(entry.group, items);
  }

  return [...groups].map(([group, items]) => ({ id: `emoji-${emojiGroups[group]}`, items }));
}

// The recent picks that still exist, newest first.
function recentSection(
  recent: readonly string[],
  known: ReadonlyMap<string, Suggestion>,
): PickerSection[] {
  const items = recent.flatMap((insert) => {
    const item = known.get(insert);

    return item
      ? [item]
      : [];
  });

  return items.length > 0
    ? [{ id: 'recent', items }]
    : [];
}

export function pickerSections(
  emotes: readonly ChatEmoteEntry[],
  emoji: readonly EmojiEntry[],
  recent: readonly string[],
  query: string,
): readonly PickerSection[] {
  const search = query.trim();

  if (search !== '') {
    const emoteHits = emoteSuggestions(emotes, search, pickerSearchLimit);
    const emojiHits = emojiSuggestions(emoji, search, pickerSearchLimit);

    return [{ id: 'results', items: merge(emoteHits, emojiHits).slice(0, pickerSearchLimit) }];
  }

  const sections = merge(emoteSections(emotes), emojiSections(emoji));
  const known = new Map(sections.flatMap((section) =>
    section.items).map((item) =>
    [item.insert, item]));

  return merge(recentSection(recent, known), sections);
}

export function remember(recent: readonly string[], insert: string): string[] {
  const others = recent.filter((item) =>
    item !== insert);

  return merge([insert], others).slice(0, recentLimit);
}
