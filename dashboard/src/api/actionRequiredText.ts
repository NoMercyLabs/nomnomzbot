// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { clockTime } from '@/lib/time';

import type { ActionRequiredItem } from './actionRequired';

// The server sends an attention item as words KEYS plus named parameters, never prose. This file
// turns one into the title, the message and the page to open. It is the Vue twin of the app's
// AttentionText.kt. It sits beside the API type because it reads that type and nothing else.

export interface AttentionWords {
  title: string;
  message: string | null;
  route: string | null;
}

export interface AttentionTranslator {
  t: (key: string, named?: Record<string, unknown>) => string;
  te: (key: string) => boolean;
}

const WORDS = 'frame.inbox.attention.';

// These titles come in a one and a many form, picked by the item count.
const COUNTED_TITLES: readonly string[] = [
  'attention_held_title',
  'attention_unmanaged_rewards_title',
  'attention_song_lost_title',
];

// The dashboard pages (router children of /app) an attention item can send the reader to.
export const attentionPages = [
  'chat',
  'commands',
  'music',
  'overlays',
  'people',
  'scripts',
  'settings',
  'stream',
  'tts',
] as const;

const PROVIDER_NAMES: Record<string, string> = {
  spotify: 'Spotify',
  youtube: 'YouTube',
  discord: 'Discord',
  kick: 'Kick',
  kick_bot: 'Kick',
};

const SOURCE_NAMES = {
  unban_requests: 'attention_source_name_unban_requests',
} as const satisfies Record<string, string>;

function providerName(provider: string): string {
  return PROVIDER_NAMES[provider] ?? provider.charAt(0).toUpperCase() + provider.slice(1);
}

function titleKeyOf(item: ActionRequiredItem): string {
  if (!COUNTED_TITLES.includes(item.titleKey)) {
    return item.titleKey;
  }

  return Number(item.count) > 1
    ? `${item.titleKey}_many`
    : `${item.titleKey}_one`;
}

function messageKeyOf(item: ActionRequiredItem): string {
  const noDetail =
    item.messageKey === 'attention_shared_ban_twitch_ban_failed_message' &&
    !item.parameters.detail?.trim();

  return noDetail
    ? 'attention_shared_ban_twitch_ban_failed_no_detail_message'
    : item.messageKey;
}

function titleNamed(
  item: ActionRequiredItem,
  i18n: AttentionTranslator,
  now: Date,
): Record<string, unknown> {
  const { provider = '', source = '', until } = item.parameters;
  const sourceKey =
    SOURCE_NAMES[source as keyof typeof SOURCE_NAMES] ?? 'attention_source_name_unknown';

  return {
    ...item.parameters,
    count: Number(item.count),
    provider: providerName(provider),
    until: clockTime(until, now),
    source: i18n.t(WORDS + sourceKey),
  };
}

function messageNamed(
  item: ActionRequiredItem,
  i18n: AttentionTranslator,
): Record<string, unknown> {
  const category = item.parameters.category?.trim();
  // The words close the sentence themselves, so a period the server error ends with would double.
  const error = item.parameters.error?.trim().replace(/\.+$/, '');

  return {
    ...item.parameters,
    category: category || i18n.t(`${WORDS}attention_held_category_unknown`),
    error,
  };
}

// The in-app path for a server route such as "people/overview", or null when the Vue
// dashboard has no such page (the Open button then stays hidden).
function routeOf(deepLinkRoute: string): string | null {
  const path = deepLinkRoute.trim().replace(/^\/+/, '').toLowerCase();
  const [page] = path.split('/');
  const known = attentionPages.some((name) => name === page);

  return known
    ? `/app/${page}`
    : null;
}

export function attentionWords(
  item: ActionRequiredItem,
  i18n: AttentionTranslator,
  now: Date = new Date(),
): AttentionWords {
  const titlePath = WORDS + titleKeyOf(item);
  const messagePath = WORDS + messageKeyOf(item);
  const title = i18n.te(titlePath)
    ? i18n.t(titlePath, titleNamed(item, i18n, now))
    : i18n.t(`${WORDS}attention_unknown_title`);
  const message = i18n.te(messagePath)
    ? i18n.t(messagePath, messageNamed(item, i18n))
    : null;

  return {
    title,
    message,
    route: routeOf(item.deepLinkRoute),
  };
}
