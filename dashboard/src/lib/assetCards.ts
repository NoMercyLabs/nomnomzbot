// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ChatEmoteEntry, ChatLine } from '@/api/chat';
import type { KeyValueItem } from '@/components/layout/KeyValueList.vue';

type Fragment = ChatLine['fragments'][number];
type Badge = ChatLine['badges'][number];
type Translate = (key: string, values?: Record<string, string | number>) => string;

// What a chat line and the emote catalog both know of an emote; only the catalog knows a Twitch tier.
type EmoteFacts = {
  id?: string | null;
  provider: string;
  animated: boolean;
  zeroWidth: boolean;
  tier?: string | number | null;
  owner?: EmotePersonFacts | null;
  artist?: EmotePersonFacts | null;
};

type EmotePersonFacts = {
  id?: string | null;
  login?: string | null;
  displayName?: string | null;
  profileUrl?: string | null;
};

// Who made or owns an asset. A Twitch user id opens their profile here; anyone else links to their own page.
export type AssetPerson = KeyValueItem & {
  role: 'owner' | 'artist';
  userId: string | null;
  href: string | null;
};

export type AssetCard = {
  title: string;
  image: string;
  facts: KeyValueItem[];
  people: AssetPerson[];
  link: { href: string; label: string } | null;
};

// The page of an emote on its own site. Twitch has no public page per emote, so it gets none.
const emotePages = {
  SevenTv: 'https://7tv.app/emotes/',
  Bttv: 'https://betterttv.com/emotes/',
  Ffz: 'https://www.frankerfacez.com/emoticon/',
} as const satisfies Record<string, string>;

export type EmoteImages = {
  urls: Record<string, string>;
  staticUrls?: null | Record<string, string>;
};

// Emotes always move, like on Twitch; the system's less-motion setting does not freeze chat content.
export function emoteFrames(emote: EmoteImages): Record<string, string> {
  return emote.urls;
}

// The server already reads Twitch's tier code ("1000") as its number, 1 to 3.
export function emoteTier(tier: string | number | null | undefined): 1 | 2 | 3 | null {
  const n = Number(tier);

  return n === 1 || n === 2 || n === 3
    ? n
    : null;
}

// The image at the smallest scale, the size a chat line shows.
export function smallestUrl(urls: Record<string, string>): string | undefined {
  return urls['1'] ?? Object.values(urls)[0];
}

// The image at the largest scale the server sent; the keys are the scales "1" to "4".
export function largestUrl(urls: Record<string, string>): string | undefined {
  const scales = Object.keys(urls).sort((a, b) =>
    Number(b) - Number(a));

  return scales.length > 0
    ? urls[scales[0]!]
    : undefined;
}

function emotePage(emote: EmoteFacts): string | null {
  const base: string | undefined = emotePages[emote.provider as keyof typeof emotePages];

  return base && emote.id
    ? base + emote.id
    : null;
}

export function emoteCard(fragment: Fragment, t: Translate): AssetCard | null {
  const emote = fragment.emote;
  const image = emote
    ? largestUrl(emoteFrames(emote))
    : undefined;

  if (!emote || !image)
    return null;

  return emoteAssetCard(emote, fragment.text, image, t);
}

// The card for an emote from the catalog, such as the ones a person's own channel offers.
export function catalogEmoteCard(emote: ChatEmoteEntry, t: Translate): AssetCard | null {
  const image = largestUrl(emoteFrames(emote));

  if (!image)
    return null;

  const owner = emote.ownerId
    ? {
        id: emote.ownerId,
        login: emote.ownerLogin,
        displayName: emote.ownerName,
      }
    : null;

  return emoteAssetCard({ ...emote, owner }, emote.code, image, t);
}

// Only a Twitch emote's owner id is a Twitch user id; a 7TV, BTTV or FFZ id belongs to that site.
function emotePerson(
  emote: EmoteFacts,
  role: AssetPerson['role'],
  t: Translate,
): AssetPerson | null {
  const person = emote[role];
  const name = person?.displayName ?? person?.login;

  if (!person || !name)
    return null;

  const twitch = emote.provider === 'Twitch';
  const label = role === 'artist'
    ? 'asset.people.artist'
    : twitch
      ? 'asset.people.channel'
      : 'asset.people.uploader';

  return {
    role,
    label: t(label),
    value: name,
    userId: twitch
      ? person.id ?? null
      : null,
    href: person.profileUrl ?? null,
  };
}

function emoteAssetCard(emote: EmoteFacts, title: string, image: string, t: Translate): AssetCard {
  const facts: KeyValueItem[] = [
    { label: t('asset.provider'), value: t(`asset.providers.${emote.provider}`) },
    { label: t('asset.kind'), value: t(emote.animated
      ? 'asset.animated'
      : 'asset.still') },
  ];

  if (emote.zeroWidth) {
    facts.push({ label: t('asset.layer'), value: t('asset.zeroWidth') });
  }

  const tier = emoteTier(emote.tier);

  if (tier !== null) {
    facts.push({ label: t('asset.tier'), value: t('asset.tierNumber', { n: tier }) });
  }

  const page = emotePage(emote);

  return {
    title,
    image,
    facts,
    people: [emotePerson(emote, 'owner', t), emotePerson(emote, 'artist', t)]
      .filter((person): person is AssetPerson =>
        person !== null),
    link: page
      ? { href: page, label: t(`asset.open.${emote.provider}`) }
      : null,
  };
}

// The server sends a badge's set and version; badge info is the number Twitch adds, such as subscribed months.
export function badgeCard(badge: Badge, t: Translate): AssetCard | null {
  const image = largestUrl(badge.urls);

  if (!image)
    return null;

  const facts: KeyValueItem[] = [
    { label: t('asset.set'), value: badge.setId },
    { label: t('asset.version'), value: badge.id },
  ];

  if (badge.info) {
    facts.push({ label: t('asset.info'), value: badge.info });
  }

  return {
    title: badge.setId,
    image,
    facts,
    people: [],
    link: null,
  };
}
