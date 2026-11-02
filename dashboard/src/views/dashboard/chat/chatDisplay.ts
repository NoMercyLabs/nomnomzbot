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

// How the chat feed looks to this viewer. The choice is kept on this device (no per-user
// preferences endpoint exists yet), under one key, so a remount and a reload both find it.
import { useLocalStorage } from '@vueuse/core';
import type { RemovableRef } from '@vueuse/core';

import type { ChatLine } from '@/api/chat';
import { readableNameColor } from '@/lib/accent';

export type TimestampStyle = 'off' | '24' | '12';
export type NameColors = 'sent' | 'readable' | 'off';
export type Spacing = 'tight' | 'normal' | 'roomy';
export type EmoteSize = 's' | 'm' | 'l';
export type RowLayout = 'one' | 'two';
export type DeletedLook = 'hide' | 'strike' | 'show';
// How many days old an account still counts as new.
export type NewAccountDays = '1' | '7' | '30' | '90';

export type ChatDisplay = {
  hintFirstEver: boolean;
  hintFirstOfDay: boolean;
  hintFirstOfStream: boolean;
  hintNewAccount: boolean;
  hintMessageType: boolean;
  newAccountDays: NewAccountDays;
  timestamps: TimestampStyle;
  badges: boolean;
  avatars: boolean;
  platform: boolean;
  commands: boolean;
  alternating: boolean;
  nameColors: NameColors;
  narrow: boolean;
  spacing: Spacing;
  rows: RowLayout;
  emoteSize: EmoteSize;
  mentions: boolean;
  deleted: DeletedLook;
  htmlAsText: boolean;
};

export const chatDisplayDefaults = {
  // The first message of a day or a stream is every chatter's, every day: off until asked for.
  hintFirstEver: true,
  hintFirstOfDay: false,
  hintFirstOfStream: false,
  hintNewAccount: true,
  hintMessageType: true,
  newAccountDays: '7',
  timestamps: '24',
  badges: true,
  avatars: false,
  platform: false,
  commands: true,
  alternating: false,
  nameColors: 'readable',
  narrow: false,
  spacing: 'normal',
  rows: 'one',
  emoteSize: 'm',
  mentions: true,
  deleted: 'strike',
  htmlAsText: false,
} as const satisfies ChatDisplay;

// A hint on a line: its i18n key under dashboard.chat.hint, and the account age in days for a new account.
export type LineHint = { key: string; days?: number };

const messageTypeHints = {
  channel_points_highlighted: 'highlighted',
  channel_points_sub_only: 'subOnly',
  user_intro: 'intro',
} as const satisfies Record<string, string>;

const dayMs = 86_400_000;

// The hints a line shows under this viewer's Display choices. First ever already says first of the day and of the
// stream, so only the most specific "first" shows.
export function lineHints(
  line: Pick<ChatLine, 'hints' | 'messageType'>,
  display: Pick<
    ChatDisplay,
    | 'hintFirstEver' |
    'hintFirstOfDay' |
    'hintFirstOfStream' |
    'hintNewAccount' |
    'hintMessageType' |
    'newAccountDays'
  >,
  now: Date,
): LineHint[] {
  const found: LineHint[] = [];
  const facts = line.hints;

  if (facts) {
    if (facts.firstEver && display.hintFirstEver) {
      found.push({ key: 'firstEver' });
    } else if (!facts.firstEver) {
      if (facts.firstOfStream && display.hintFirstOfStream) {
        found.push({ key: 'firstOfStream' });
      } else if (facts.firstOfDay && display.hintFirstOfDay) {
        found.push({ key: 'firstOfDay' });
      }
    }

    if (display.hintNewAccount && facts.accountCreatedAt) {
      const days = Math.floor((now.getTime() - Date.parse(facts.accountCreatedAt)) / dayMs);

      if (days >= 0 && days < Number(display.newAccountDays)) {
        found.push({ key: 'newAccount', days });
      }
    }
  }

  const byType = (messageTypeHints as Record<string, string | undefined>)[line.messageType];

  if (display.hintMessageType && byType) {
    found.push({ key: byType });
  }

  return found;
}

export const emoteHeights = {
  s: '1.2em',
  m: '1.5em',
  l: '2.3em',
} as const satisfies Record<EmoteSize, string>;

// The emote of a Gigantified Emote Power-up.
export const gigantifiedEmoteHeight = '5rem';

// Every part of a line centers on one line height: the emote height on a line with emotes, else 1.5rem.
export function emoteVars(
  line: Pick<ChatLine, 'fragments' | 'messageType'>,
  display: Pick<ChatDisplay, 'emoteSize'>,
) {
  // A Gigantified Emote Power-up shows its emote large, like Twitch does.
  const emoteHeight = line.messageType === 'power_ups_gigantified_emote'
    ? gigantifiedEmoteHeight
    : emoteHeights[display.emoteSize];
  const hasEmote = line.fragments.some((fragment) =>
    fragment.emote || fragment.type === 'html');

  return {
    '--emote-h': emoteHeight,
    '--line-h': hasEmote
      ? `max(1.5rem, ${emoteHeight})`
      : '1.5rem',
  } as const satisfies { '--emote-h': string; '--line-h': string };
}

// A chatter color as the Display option asks: off, as sent, or lifted to a readable contrast.
export function shownNameColor(
  color: string | null | undefined,
  nameColors: NameColors,
): string | undefined {
  if (!color || nameColors === 'off')
    return undefined;

  if (nameColors === 'sent')
    return color;

  const theme = document.documentElement.classList.contains('light')
    ? 'light'
    : 'dark';

  return readableNameColor(color, theme, 4.5) ?? undefined;
}

export const spacingPadding = {
  tight: 'py-0.5',
  normal: 'py-2',
  roomy: 'py-4',
} as const satisfies Record<Spacing, string>;

export function useChatDisplay(): RemovableRef<ChatDisplay> {
  return useLocalStorage<ChatDisplay>('nnz.chat.display', { ...chatDisplayDefaults }, {
    mergeDefaults: true,
  });
}

// mention: the part names `username` (me); at: the part is an @name, which shows bold like FrankerFaceZ.
export type MentionPart = {
  text: string;
  mention: boolean;
  at: boolean;
};

// Cuts a text at every @name and at every place it names `username` (with or without the @).
export function splitMentions(text: string, username: string | null): MentionPart[] {
  const escaped = username?.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const pattern = escaped
    ? new RegExp(`(@\\w+|\\b${escaped}\\b)`, 'i')
    : /(@\w+)/;
  const me = escaped
    ? new RegExp(`^@?${escaped}$`, 'i')
    : null;

  return text
    .split(pattern)
    .filter((part) => part !== '')
    .map((part) => ({
      text: part,
      mention: me?.test(part) ?? false,
      at: part.startsWith('@'),
    }));
}
