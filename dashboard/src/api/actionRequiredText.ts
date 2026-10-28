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

import { lookup, oneOf } from '@/lib/array';
import { clockTime } from '@/lib/time';

import type { ActionRequiredItem } from './actionRequired';

// The server sends an attention item as words KEYS plus named parameters, never prose. This file
// turns one into the title, the message and the page to open. It is the Vue twin of the app's
// AttentionText.kt. It sits beside the API type because it reads that type and nothing else.

export interface AttentionWords {
  title: string;
  message: string | null;
  open: string | null;
  route: string | null;
}

export interface AttentionTranslator {
  t: (key: string, named?: Record<string, string | number>) => string;
  te: (key: string) => boolean;
  locale: { readonly value: string };
}

const WORDS = 'frame.inbox.attention.';

// These titles come in a one and a many form, picked by the item count.
const COUNTED_TITLES = [
  'attention_held_title',
  'attention_unmanaged_rewards_title',
  'attention_song_lost_title',
] as const satisfies readonly string[];

// The dashboard pages (router children of /app) an attention item can send the reader to.
export const attentionPages = [
  'chat',
  'commands',
  'music',
  'obs',
  'overlays',
  'people',
  'scripts',
  'settings',
  'stream',
  'tts',
] as const;

const PROVIDER_NAMES = {
  spotify: 'Spotify',
  youtube: 'YouTube',
  discord: 'Discord',
  kick: 'Kick',
  kick_bot: 'Kick',
} as const satisfies Record<string, string>;

const SOURCE_NAMES = {
  unban_requests: 'attention_source_name_unban_requests',
} as const satisfies Record<string, string>;

function providerName(provider: string): string {
  return lookup(PROVIDER_NAMES, provider) ?? provider.charAt(0).toUpperCase() + provider.slice(1);
}

function titleKeyOf(item: ActionRequiredItem): string {
  const counted = oneOf(COUNTED_TITLES, item.titleKey);

  if (counted === undefined) {
    return item.titleKey;
  }

  return Number(item.count) > 1
    ? `${counted}_many`
    : `${counted}_one`;
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
) {
  const { provider = '', source = '', until } = item.parameters;
  const sourceKey = lookup(SOURCE_NAMES, source) ?? 'attention_source_name_unknown';

  return {
    ...item.parameters,
    count: Number(item.count),
    provider: providerName(provider),
    until: clockTime(until, now, i18n.locale.value),
    source: i18n.t(WORDS + sourceKey),
  } as const satisfies Record<string, string | number>;
}

function messageNamed(
  item: ActionRequiredItem,
  i18n: AttentionTranslator,
) {
  const category = item.parameters.category?.trim();
  // The words close the sentence themselves, so a period the server error ends with would double.
  const error = item.parameters.error?.trim().replace(/\.+$/, '');

  return {
    ...item.parameters,
    count: Number(item.count),
    category: category || i18n.t(`${WORDS}attention_held_category_unknown`),
    error: error ?? '',
  } as const satisfies Record<string, string | number>;
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
) {
  const titlePath = WORDS + titleKeyOf(item);
  const messagePath = WORDS + messageKeyOf(item);
  const title = i18n.te(titlePath)
    ? i18n.t(titlePath, titleNamed(item, i18n, now))
    : i18n.t(`${WORDS}attention_unknown_title`);
  const message = i18n.te(messagePath)
    ? i18n.t(messagePath, messageNamed(item, i18n))
    : null;

  // A notice can name its own Open button, such as "Open OBS"; the others use the shared word.
  const openPath = `${WORDS}${item.titleKey.replace(/_title$/, '_open')}`;

  return {
    title,
    message,
    open: i18n.te(openPath)
      ? i18n.t(openPath)
      : null,
    route: routeOf(item.deepLinkRoute),
  } as const satisfies AttentionWords;
}
