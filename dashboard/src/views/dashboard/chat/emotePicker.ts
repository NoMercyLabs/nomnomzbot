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

// The picker by owner, as Stoney asked on 2026-10-14: favorites, recent picks, your own emotes, this channel's,
// every other channel by name, the global ones, then the emoji per Unicode group. A search turns it into one
// list of every match. A section of one channel carries that channel's name.
export type PickerSection = {
  id: string;
  items: Suggestion[];
  name?: string;
};

// The name of the channel being viewed, and the favorites as favoriteKey values, newest first.
export type PickerContext = {
  channelName: string | null;
  favorites: readonly string[];
};

export const pickerSearchLimit = 120;
export const recentLimit = 24;

const providerOrder = ['Twitch', 'SevenTv', 'Bttv', 'Ffz'] as const satisfies readonly string[];

export function favoriteKey(provider: string, emoteId: string): string {
  return `${provider}:${emoteId}`;
}

function providerRank(emote: ChatEmoteEntry): number {
  return providerOrder.indexOf(emote.provider as typeof providerOrder[number]);
}

// The server says whose an emote is (own, channel, other or global); another channel's emotes group by it.
function sectionOf(emote: ChatEmoteEntry): string {
  if (emote.origin === 'own' || emote.origin === 'channel' || emote.origin === 'global')
    return emote.origin;

  return emote.origin === 'other' && emote.ownerLogin
    ? `owner-${emote.ownerLogin.toLowerCase()}`
    : 'others';
}

function ownerSections(
  emotes: readonly ChatEmoteEntry[],
  at: PickerContext,
): readonly PickerSection[] {
  const groups = new Map<string, PickerSection>();
  const ordered = [...emotes].sort((a, b) =>
    providerRank(a) - providerRank(b));

  for (const emote of ordered) {
    const id = sectionOf(emote);
    // Only another channel's section is named after its owner; a 7TV emote's owner is its uploader.
    const section = groups.get(id) ?? {
      id,
      items: [],
      name: id.startsWith('owner-')
        ? emote.ownerName ?? emote.ownerLogin ?? undefined
        : undefined,
    };

    section.items.push(emoteSuggestion(emote));
    groups.set(id, section);
  }

  const channel = groups.get('channel');

  if (channel) {
    channel.name = at.channelName ?? undefined;
  }

  const others = [...groups.values()]
    .filter((section) =>
      section.id.startsWith('owner-'))
    .sort((a, b) =>
      (a.name ?? a.id).localeCompare(b.name ?? b.id));

  const fixed = (id: string): PickerSection[] => {
    const section = groups.get(id);

    return section
      ? [section]
      : [];
  };

  return merge(fixed('own'), fixed('channel'), others, fixed('others'), fixed('global'));
}

// The favorites that are usable here, in the order given.
function favoriteSection(
  emotes: readonly ChatEmoteEntry[],
  favorites: readonly string[],
): PickerSection[] {
  const byKey = new Map(emotes.flatMap((emote) =>
    emote.id
      ? [[favoriteKey(emote.provider, emote.id), emote] as const]
      : []));
  const items = favorites.flatMap((key) => {
    const emote = byKey.get(key);

    return emote
      ? [emoteSuggestion(emote)]
      : [];
  });

  return items.length > 0
    ? [{ id: 'favorites', items }]
    : [];
}

// The Unicode emoji groups in their order. Group 2 holds skin tones and hair parts, which are no emoji on their own.
const emojiGroups = [
  'smileys', 'people', 'component', 'animals', 'food', 'travel', 'activities', 'objects', 'symbols', 'flags',
] as const satisfies readonly string[];
const componentGroup = 2;

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
  at: PickerContext,
): readonly PickerSection[] {
  const search = query.trim();

  if (search !== '') {
    const emoteHits = emoteSuggestions(emotes, search, pickerSearchLimit);
    const emojiHits = emojiSuggestions(emoji, search, pickerSearchLimit);

    return [{ id: 'results', items: merge(emoteHits, emojiHits).slice(0, pickerSearchLimit) }];
  }

  const sections = merge(ownerSections(emotes, at), emojiSections(emoji));
  const known = new Map(sections.flatMap((section) =>
    section.items).map((item) =>
    [item.insert, item]));

  return merge(favoriteSection(emotes, at.favorites), recentSection(recent, known), sections);
}

export function remember(recent: readonly string[], insert: string): string[] {
  const others = recent.filter((item) =>
    item !== insert);

  return merge([insert], others).slice(0, recentLimit);
}
