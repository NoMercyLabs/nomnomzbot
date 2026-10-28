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

import Bell from '~icons/nnz/bell-ring';
import Bookmark from '~icons/nnz/bookmark';
import BookmarkAdd from '~icons/nnz/bookmark-add';
import Chat from '~icons/nnz/chat-bubble';
import Check from '~icons/nnz/check';
import Clock from '~icons/nnz/clock';
import Coins from '~icons/nnz/coins';
import Crown from '~icons/nnz/crown';
import Devices from '~icons/nnz/devices';
import Eye from '~icons/nnz/eye';
import Fire from '~icons/nnz/fire';
import Flag from '~icons/nnz/flag';
import Forward from '~icons/nnz/forward';
import Heart from '~icons/nnz/heart';
import HeartBroken from '~icons/nnz/heart-broken';
import HeartFilled from '~icons/nnz/heart-filled';
import Lightning from '~icons/nnz/lightning';
import Link from '~icons/nnz/link';
import MediaPlayer from '~icons/nnz/media-player';
import Megaphone from '~icons/nnz/megaphone';
import Mic from '~icons/nnz/mic';
import Moderator from '~icons/nnz/moderator';
import Next from '~icons/nnz/next';
import Music from '~icons/nnz/note-eighth-pair';
import Pause from '~icons/nnz/pause';
import Pin from '~icons/nnz/pin';
import Play from '~icons/nnz/play';
import PlayCircle from '~icons/nnz/play-circle';
import PlaylistAdd from '~icons/nnz/playlist-add';
import PlaylistRemove from '~icons/nnz/playlist-remove';
import Previous from '~icons/nnz/previous';
import Repeat from '~icons/nnz/repeat';
import RepeatOne from '~icons/nnz/repeat-one';
import Shield from '~icons/nnz/shield';
import Shuffle from '~icons/nnz/shuffle';
import Smile from '~icons/nnz/smile';
import Sparkles from '~icons/nnz/sparkles';
import Star from '~icons/nnz/star';
import Timer from '~icons/nnz/timer';
import UserAdd from '~icons/nnz/user-add';
import UserRemove from '~icons/nnz/user-remove';
import VolumeMuted from '~icons/nnz/volume-muted';
import VolumeOne from '~icons/nnz/volume-one';
import VolumeThree from '~icons/nnz/volume-three';
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
  devices: Devices,
  eye: Eye,
  fire: Fire,
  flag: Flag,
  forward: Forward,
  heart: Heart,
  'heart-broken': HeartBroken,
  'heart-filled': HeartFilled,
  lightning: Lightning,
  link: Link,
  'media-player': MediaPlayer,
  megaphone: Megaphone,
  mic: Mic,
  moderator: Moderator,
  next: Next,
  'note-eighth-pair': Music,
  pause: Pause,
  pin: Pin,
  play: Play,
  'play-circle': PlayCircle,
  'playlist-add': PlaylistAdd,
  'playlist-remove': PlaylistRemove,
  previous: Previous,
  repeat: Repeat,
  'repeat-one': RepeatOne,
  shield: Shield,
  shuffle: Shuffle,
  smile: Smile,
  sparkles: Sparkles,
  star: Star,
  timer: Timer,
  'user-add': UserAdd,
  'user-remove': UserRemove,
  'volume-muted': VolumeMuted,
  'volume-one': VolumeOne,
  'volume-three': VolumeThree,
  'volume-two': Volume,
} as const satisfies Record<string, Component>;

export function playerIcon(key: string): Component {
  return lookup(playerIcons, key) ?? Lightning;
}

// The keys the editor offers, in the table's order.
export const PLAYER_ICON_KEYS = Object.keys(playerIcons);
