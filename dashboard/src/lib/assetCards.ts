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
import { tierNumber } from '@/components/layout/profile/profileRoles';
import { useReducedMotion } from '@/composables/useReducedMotion';

type Fragment = ChatLine['fragments'][number];
type Badge = ChatLine['badges'][number];
type Translate = (key: string, values?: Record<string, unknown>) => string;

// What a chat line and the emote catalog both know of an emote; only the catalog knows a Twitch tier.
type EmoteFacts = {
  id?: string | null;
  provider: string;
  animated: boolean;
  zeroWidth: boolean;
  tier?: string | number | null;
};

export type AssetCard = {
  title: string;
  image: string;
  facts: KeyValueItem[];
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

// The still frames when the reader asked for less motion; a still emote has the same frames either way.
export function emoteFrames(emote: EmoteImages): Record<string, string> {
  return useReducedMotion().value && emote.staticUrls
    ? emote.staticUrls
    : emote.urls;
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

  return emoteAssetCard(emote, emote.code, image, t);
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

  const tier = tierNumber(emote.tier);

  if (tier !== null) {
    facts.push({ label: t('asset.tier'), value: t('asset.tierNumber', { n: tier }) });
  }

  const page = emotePage(emote);

  return {
    title,
    image,
    facts,
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
    link: null,
  };
}
