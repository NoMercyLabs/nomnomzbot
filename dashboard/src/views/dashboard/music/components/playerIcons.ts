// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import Bell from '~icons/nnz/bell-ring';
import Bookmark from '~icons/nnz/bookmark';
import BookmarkAdd from '~icons/nnz/bookmark-add';
import Chat from '~icons/nnz/chat-bubble';
import Check from '~icons/nnz/check';
import Clock from '~icons/nnz/clock';
import Coins from '~icons/nnz/coins';
import Crown from '~icons/nnz/crown';
import Eye from '~icons/nnz/eye';
import Fire from '~icons/nnz/fire';
import Flag from '~icons/nnz/flag';
import Heart from '~icons/nnz/heart';
import HeartFilled from '~icons/nnz/heart-filled';
import Lightning from '~icons/nnz/lightning';
import Link from '~icons/nnz/link';
import Megaphone from '~icons/nnz/megaphone';
import Mic from '~icons/nnz/mic';
import Moderator from '~icons/nnz/moderator';
import Music from '~icons/nnz/note-eighth-pair';
import Pin from '~icons/nnz/pin';
import PlaylistAdd from '~icons/nnz/playlist-add';
import Shield from '~icons/nnz/shield';
import Smile from '~icons/nnz/smile';
import Sparkles from '~icons/nnz/sparkles';
import Star from '~icons/nnz/star';
import Timer from '~icons/nnz/timer';
import Volume from '~icons/nnz/volume-two';
import type { Component } from 'vue';

import { lookup } from '@/lib/array';

// The icons a streamer can pin to a player button. The server stores the file name; a name this table
// does not know still gets a button, with the lightning bolt.
const playerIcons = {
  'bell-ring': Bell,
  bookmark: Bookmark,
  'bookmark-add': BookmarkAdd,
  'chat-bubble': Chat,
  check: Check,
  clock: Clock,
  coins: Coins,
  crown: Crown,
  eye: Eye,
  fire: Fire,
  flag: Flag,
  heart: Heart,
  'heart-filled': HeartFilled,
  lightning: Lightning,
  link: Link,
  megaphone: Megaphone,
  mic: Mic,
  moderator: Moderator,
  'note-eighth-pair': Music,
  pin: Pin,
  'playlist-add': PlaylistAdd,
  shield: Shield,
  smile: Smile,
  sparkles: Sparkles,
  star: Star,
  timer: Timer,
  'volume-two': Volume,
} as const satisfies Record<string, Component>;

export function playerIcon(key: string): Component {
  return lookup(playerIcons, key) ?? Lightning;
}
