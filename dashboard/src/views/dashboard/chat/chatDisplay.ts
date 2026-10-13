// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// How the chat feed looks to this viewer. The choice is kept on this device (no per-user
// preferences endpoint exists yet), under one key, so a remount and a reload both find it.
import { useLocalStorage } from '@vueuse/core';
import type { RemovableRef } from '@vueuse/core';

export type TimestampStyle = 'off' | '24' | '12';
export type NameColors = 'sent' | 'readable' | 'off';
export type Spacing = 'tight' | 'normal' | 'roomy';
export type EmoteSize = 's' | 'm' | 'l';
export type DeletedLook = 'hide' | 'strike' | 'show';

export type ChatDisplay = {
  timestamps: TimestampStyle;
  badges: boolean;
  avatars: boolean;
  platform: boolean;
  commands: boolean;
  alternating: boolean;
  nameColors: NameColors;
  narrow: boolean;
  spacing: Spacing;
  emoteSize: EmoteSize;
  mentions: boolean;
  deleted: DeletedLook;
};

export const chatDisplayDefaults = {
  timestamps: '24',
  badges: true,
  avatars: false,
  platform: false,
  commands: true,
  alternating: false,
  nameColors: 'readable',
  narrow: false,
  spacing: 'normal',
  emoteSize: 'm',
  mentions: true,
  deleted: 'strike',
} as const satisfies ChatDisplay;

export const emoteHeights = {
  s: '1.2em',
  m: '1.5em',
  l: '2.3em',
} as const satisfies Record<EmoteSize, string>;

export const spacingPadding = {
  tight: 'py-0.5',
  normal: 'py-2',
  roomy: 'py-4',
} as const satisfies Record<Spacing, string>;

// Lifts the line tray past the row padding above, so it sits on the row's top edge with a 2px overlap.
export const trayLift = {
  tight: '-translate-y-0',
  normal: '-translate-y-1.5',
  roomy: '-translate-y-3.5',
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
    .filter((part: string) => part !== '')
    .map((part: string) => ({
      text: part,
      mention: me?.test(part) ?? false,
      at: part.startsWith('@'),
    }));
}
