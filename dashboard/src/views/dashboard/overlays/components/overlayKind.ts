// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component } from 'vue';
import BellRing from '~icons/nnz/bell-ring';
import ChatMessages from '~icons/nnz/chat-messages';
import NoteEighthPair from '~icons/nnz/note-eighth-pair';
import WaveForm from '~icons/nnz/wave-form';

export interface OverlayKind {
  /** Key under dashboard.overlays.kinds. */
  key: string;
  /** Null when the kind has no clear picture: the tile stays, empty, so rows line up. */
  icon: Component | null;
}

interface KindRule extends OverlayKind {
  events: readonly string[];
}

// The kind comes from the events the overlay listens to, never from its name or title.
// The first rule with an event the overlay listens to wins, so the order is the priority.
const RULES = [
  {
    key: 'tts',
    icon: WaveForm,
    events: ['tts_speak'],
  },
  {
    key: 'chat',
    icon: ChatMessages,
    events: ['ChatMessage', 'ChatMessageEnriched'],
  },
  {
    key: 'nowPlaying',
    icon: NoteEighthPair,
    events: ['now_playing', 'track_saved_changed', 'spotify.track_changed'],
  },
  {
    key: 'alerts',
    icon: BellRing,
    events: ['follow', 'subscription', 'resub', 'gift', 'cheer'],
  },
] as const satisfies readonly KindRule[];

const PLAIN: OverlayKind = { key: 'overlay', icon: null };

export function overlayKind(eventSubscriptions: readonly string[]): OverlayKind {
  const rule: KindRule | undefined = RULES.find((candidate: KindRule) =>
    candidate.events.some((event) => eventSubscriptions.includes(event)),
  );

  return rule ?? PLAIN;
}

// "channel.subscription.gift" reads as "subscription gift".
export function plainEvent(key: string): string {
  return key.replace(/^(twitch|channel|overlay|spotify)\./, '').replace(/[._]/g, ' ');
}
