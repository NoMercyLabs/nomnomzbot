// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ChatLine } from '@/api/chat';
import type { KeyValueItem } from '@/components/layout/KeyValueList.vue';

type Fragment = ChatLine['fragments'][number];
type Emote = NonNullable<Fragment['emote']>;
type Badge = ChatLine['badges'][number];
type Translate = (key: string) => string;

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

// The image at the largest scale the server sent; the keys are the scales "1" to "4".
export function largestUrl(urls: Record<string, string>): string | undefined {
  const scales = Object.keys(urls).sort((a, b) =>
    Number(b) - Number(a));

  return scales.length > 0
    ? urls[scales[0]!]
    : undefined;
}

function emotePage(emote: Emote): string | null {
  const base: string | undefined = emotePages[emote.provider as keyof typeof emotePages];

  return base
    ? base + emote.id
    : null;
}

export function emoteCard(fragment: Fragment, t: Translate): AssetCard | null {
  const emote = fragment.emote;
  const image = emote
    ? largestUrl(emote.urls)
    : undefined;

  if (!emote || !image)
    return null;

  const facts: KeyValueItem[] = [
    { label: t('dashboard.chat.asset.provider'), value: t(`dashboard.chat.asset.providers.${emote.provider}`) },
    { label: t('dashboard.chat.asset.kind'), value: t(emote.animated
      ? 'dashboard.chat.asset.animated'
      : 'dashboard.chat.asset.still') },
  ];

  if (emote.zeroWidth) {
    facts.push({ label: t('dashboard.chat.asset.layer'), value: t('dashboard.chat.asset.zeroWidth') });
  }

  const page = emotePage(emote);

  return {
    title: fragment.text,
    image,
    facts,
    link: page
      ? { href: page, label: t(`dashboard.chat.asset.open.${emote.provider}`) }
      : null,
  };
}

// The server sends a badge's set and version; badge info is the number Twitch adds, such as subscribed months.
export function badgeCard(badge: Badge, t: Translate): AssetCard | null {
  const image = largestUrl(badge.urls);

  if (!image)
    return null;

  const facts: KeyValueItem[] = [
    { label: t('dashboard.chat.asset.set'), value: badge.setId },
    { label: t('dashboard.chat.asset.version'), value: badge.id },
  ];

  if (badge.info) {
    facts.push({ label: t('dashboard.chat.asset.info'), value: badge.info });
  }

  return {
    title: badge.setId,
    image,
    facts,
    link: null,
  };
}
