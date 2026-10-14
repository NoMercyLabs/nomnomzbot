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
// every other channel by name, the global ones, then the emoji per Unicode group. A search keeps every section,
// each with only its matches. A section of one channel carries that channel's name and avatar.
export type PickerSection = {
  id: string;
  items: Suggestion[];
  name?: string;
  avatar?: string;
};

// The channel being viewed, the avatars of that channel and of you, and the favorites as favoriteKey values,
// newest first.
export type PickerContext = {
  channelName: string | null;
  channelAvatar?: string | null;
  ownAvatar?: string | null;
  ownName?: string | null;
  favorites: readonly string[];
};

export const recentLimit = 24;

const providerOrder = ['Twitch', 'SevenTv', 'Bttv', 'Ffz'] as const satisfies readonly string[];

// The source the server stores a starred emoji under.
export const emojiProvider = 'Emoji';

// The id a star is kept under: the emote's id, or its code when the source sent none.
export function starId(emote: ChatEmoteEntry): string {
  return emote.id ?? emote.code;
}

export function favoriteKey(provider: string, emoteId: string): string {
  return `${provider}:${emoteId}`;
}

function providerRank(emote: ChatEmoteEntry): number {
  return providerOrder.indexOf(emote.provider as typeof providerOrder[number]);
}

// The server says whose an emote is (own, channel, other or global); another channel's emotes group by it.
// The global ones and the ones without an owner keep a section per source (Twitch, 7TV, BTTV, FFZ).
// The channel's 7TV, BTTV and FFZ emotes are nobody's face: one unnamed "Channel emotes" section after the other
// channels (owner 2026-10-14: "those qualify as generic channel emotes so place them in between the channels and
// emojis"), so your own avatar never heads two sections.
function sectionOf(emote: ChatEmoteEntry): string {
  if (emote.origin === 'channel' && emote.provider !== 'Twitch')
    return 'channelExtras';

  if (emote.origin === 'own' || emote.origin === 'channel')
    return emote.origin;

  if (emote.origin === 'global')
    return `global-${emote.provider}`;

  const owner = emote.ownerLogin ?? emote.ownerId;

  return emote.origin === 'other' && owner
    ? `owner-${owner.toLowerCase()}`
    : `others-${emote.provider}`;
}

const bySource = (prefix: string): string[] =>
  providerOrder.map((provider) =>
    `${prefix}-${provider}`);

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
    const owned = id.startsWith('owner-');
    const section = groups.get(id) ?? {
      id,
      items: [],
      name: owned
        ? emote.ownerName ?? emote.ownerLogin ?? emote.ownerId ?? undefined
        : undefined,
      avatar: owned
        ? emote.ownerAvatarUrl ?? undefined
        : undefined,
    };

    section.items.push(emoteSuggestion(emote));
    groups.set(id, section);
  }

  const channel = groups.get('channel');
  const own = groups.get('own');

  if (channel) {
    channel.name = at.channelName ?? undefined;
    channel.avatar = at.channelAvatar ?? undefined;
  }

  if (own) {
    own.name = at.ownName ?? undefined;
    own.avatar = at.ownAvatar ?? undefined;
  }

  const others = [...groups.values()]
    .filter((section) =>
      section.id.startsWith('owner-'))
    .sort((a, b) =>
      (a.name ?? a.id).localeCompare(b.name ?? b.id));

  const fixed = (ids: readonly string[]): PickerSection[] =>
    ids.flatMap((id: string) => {
      const section = groups.get(id);

      return section
        ? [section]
        : [];
    });

  const unowned = fixed(bySource('others'));
  const global = fixed(bySource('global'));

  return merge(fixed(['own', 'channel']), others, fixed(['channelExtras']), unowned, global);
}

// The favorites that are usable here, in the order given.
function favoriteSection(
  emotes: readonly ChatEmoteEntry[],
  emoji: readonly EmojiEntry[],
  favorites: readonly string[],
): PickerSection[] {
  const starredEmotes = emotes.map((emote) =>
    [favoriteKey(emote.provider, starId(emote)), emoteSuggestion(emote)] as const);
  // An emoji is starred under the Emoji source, by its glyph.
  const starredEmoji = emoji.map((entry) =>
    [favoriteKey(emojiProvider, entry.glyph), emojiSuggestion(entry)] as const);
  const byKey = new Map(merge(starredEmotes, starredEmoji));
  const items = favorites.flatMap((key) => {
    const item = byKey.get(key);

    return item
      ? [item]
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

// Every match ranked as the composer ranks it, best first: an emote by its code, an emoji by its glyph.
function matchRanks(
  emotes: readonly ChatEmoteEntry[],
  emoji: readonly EmojiEntry[],
  search: string,
): ReadonlyMap<string, number> {
  const hits = merge(
    emoteSuggestions(emotes, search, Infinity),
    emojiSuggestions(emoji, search, Infinity),
  );

  return new Map(hits.map((hit, rank) =>
    [hit.insert, rank]));
}

// A search keeps every section, so the rail keeps its place; a section holds only its matches, best first.
function narrow(
  sections: readonly PickerSection[],
  ranks: ReadonlyMap<string, number>,
): PickerSection[] {
  const rankOf = (item: Suggestion): number =>
    ranks.get(item.insert) ?? -1;

  return sections.map((section) => {
    const items = section.items
      .filter((item) =>
        rankOf(item) >= 0)
      .sort((a, b) =>
        rankOf(a) - rankOf(b));

    return { ...section, items };
  });
}

export function pickerSections(
  emotes: readonly ChatEmoteEntry[],
  emoji: readonly EmojiEntry[],
  recent: readonly string[],
  query: string,
  at: PickerContext,
): readonly PickerSection[] {
  const search = query.trim();
  const sections = merge(ownerSections(emotes, at), emojiSections(emoji));
  const known = new Map(sections.flatMap((section) =>
    section.items).map((item) =>
    [item.insert, item]));
  const favorites = favoriteSection(emotes, emoji, at.favorites);
  const all = merge(favorites, recentSection(recent, known), sections);

  return search === ''
    ? all
    : narrow(all, matchRanks(emotes, emoji, search));
}

// The skin tone modifiers, light to dark: tone 1 is U+1F3FB. Tone 0 is the yellow emoji.
export const toneModifiers = ['🏻', '🏼', '🏽', '🏾', '🏿'] as const satisfies readonly string[];

function modifiersOf(glyph: string): string[] {
  return [...glyph].filter((char) =>
    (toneModifiers as readonly string[]).includes(char));
}

// The version of an emoji in one skin tone, where every person in it has that tone; the emoji as it is when it
// has no such version.
export function skinTone(item: Pick<Suggestion, 'insert' | 'skins'>, tone: number): string {
  const modifier = toneModifiers[tone - 1];

  if (!modifier)
    return item.insert;

  const toned = item.skins.find((skin) => {
    const modifiers = modifiersOf(skin);

    return modifiers.length > 0 && modifiers.every((found) =>
      found === modifier);
  });

  return toned ?? item.insert;
}

export function remember(recent: readonly string[], insert: string): string[] {
  const others = recent.filter((item) =>
    item !== insert);

  return merge([insert], others).slice(0, recentLimit);
}
