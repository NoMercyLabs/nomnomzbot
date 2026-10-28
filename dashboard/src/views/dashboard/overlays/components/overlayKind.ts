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

import type { Component } from 'vue';
import BellRing from '~icons/nnz/bell-ring';
import ChatMessages from '~icons/nnz/chat-messages';
import NoteEighthPair from '~icons/nnz/note-eighth-pair';
import WaveForm from '~icons/nnz/wave-form';
import BarChart from '~icons/nnz/bar-chart';
import Coin from '~icons/nnz/coin';
import Controller from '~icons/nnz/controller';
import Feather from '~icons/nnz/feather';
import Playlist from '~icons/nnz/playlist';
import Rocket from '~icons/nnz/rocket';
import Train from '~icons/nnz/train';
import Trophy from '~icons/nnz/trophy';

export interface OverlayKind {
  /** Key under dashboard.overlays.kinds. */
  key: string;
  /** Null when the kind has no clear picture: the row then shows no tile. */
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
  {
    key: 'reward',
    icon: Coin,
    events: ['reward_redeemed'],
  },
  {
    key: 'game',
    icon: Controller,
    events: ['game.lobby', 'game.running', 'game.resolved'],
  },
  {
    key: 'feather',
    icon: Feather,
    events: ['feather.steal', 'feather.show', 'feather.event'],
  },
  {
    key: 'hypeTrain',
    icon: Train,
    events: ['hype_train_begin', 'hype_train_progress', 'hype_train_end'],
  },
  {
    key: 'leaderboard',
    icon: Trophy,
    events: ['leaderboard.show'],
  },
  {
    key: 'poll',
    icon: BarChart,
    events: ['poll_begin', 'poll_progress', 'poll_end', 'prediction_begin', 'prediction_progress', 'prediction_lock', 'prediction_end'],
  },
  {
    key: 'raid',
    icon: Rocket,
    events: ['raid_recap.show'],
  },
  {
    key: 'songQueue',
    icon: Playlist,
    events: ['sr_queue'],
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
