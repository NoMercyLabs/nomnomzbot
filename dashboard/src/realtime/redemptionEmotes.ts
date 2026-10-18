// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The words a viewer adds to a redemption show the same emotes as the chat line that carries them.
// Twitch sends that chat line beside the redemption, so its fragments are the truth; with none to copy,
// the emote names the dashboard already loaded stand in. A word that matches nothing stays text.
import type { ChatEmoteEntry, ChatLine } from '@/api/chat';

type Fragment = ChatLine['fragments'][number];

// The chat line and the redemption of one viewer arrive within moments of each other.
const matchWindowMs = 30_000;

function isRedemption(line: ChatLine): boolean {
  return line.messageType === 'redemption';
}

function isTwin(redemption: ChatLine, chat: ChatLine): boolean {
  const apart = Math.abs(Date.parse(redemption.timestamp) - Date.parse(chat.timestamp));

  return !isRedemption(chat) &&
    chat.userId === redemption.userId &&
    chat.message === redemption.message &&
    chat.fragments.length > 0 &&
    apart <= matchWindowMs;
}

function textFragment(text: string): Fragment {
  return {
    type: 'text',
    text,
    emote: null,
    cheermote: null,
    mention: null,
    linkPreview: null,
  };
}

function emoteFragment(entry: ChatEmoteEntry, id: string): Fragment {
  return {
    ...textFragment(entry.code),
    type: 'emote',
    emote: {
      id,
      setId: entry.setId,
      format: entry.animated
        ? 'animated'
        : 'static',
      provider: entry.provider,
      urls: entry.urls,
      animated: entry.animated,
      zeroWidth: entry.zeroWidth,
      code: entry.code,
      type: entry.type,
      tier: entry.tier,
      staticUrls: entry.staticUrls,
    },
  };
}

// Words and the spaces between them, so the rebuilt text reads exactly as typed.
function emotesFromCatalog(text: string, catalog: readonly ChatEmoteEntry[]): Fragment[] {
  const byCode = new Map(catalog.map((entry) =>
    [entry.code, entry]));
  const fragments: Fragment[] = [];
  let plain = '';

  for (const piece of text.split(/(\s+)/)) {
    const entry = byCode.get(piece);
    const id = entry?.id;

    if (entry && id) {
      if (plain) {
        fragments.push(textFragment(plain));
      }

      plain = '';
      fragments.push(emoteFragment(entry, id));
    } else {
      plain += piece;
    }
  }

  if (plain) {
    fragments.push(textFragment(plain));
  }

  return fragments;
}

// The fragments a redemption's input shows: the matching chat line's, else the loaded emote names, else none.
export function redemptionFragments(
  redemption: ChatLine,
  feed: readonly ChatLine[],
  catalog: readonly ChatEmoteEntry[],
): Fragment[] {
  if (!redemption.message)
    return [];

  const twin = feed.find((chat) =>
    isTwin(redemption, chat));

  if (twin)
    return twin.fragments;

  const found = emotesFromCatalog(redemption.message, catalog);

  return found.some((fragment) =>
    fragment.emote)
    ? found
    : [];
}

// A chat line that arrives after its redemption hands the redemption its fragments.
export function adoptChatFragments(feed: readonly ChatLine[], chat: ChatLine): ChatLine[] {
  return feed.map((line) =>
    isRedemption(line) && isTwin(line, chat)
      ? { ...line, fragments: chat.fragments }
      : line);
}
