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

// The keys of the Stream Deck music plugin (tools/streamdeck/music), copied in its tray order, so the
// dashboard offers the same buttons. key is the plugin's own action name, type the bot action it runs,
// icon the designer icon that stands for it, defaults the settings the plugin key starts with. A test checks
// this list against the plugin manifest and source.
import type { JsonObject } from '@/lib/json';

export type StreamDeckAction = {
  key: string;
  type: string;
  icon: string;
  defaults?: JsonObject;
};

export const STREAM_DECK_MUSIC_ACTIONS = [
  {
    key: 'music-play',
    type: 'music_play',
    icon: 'play',
  },
  {
    key: 'music-pause',
    type: 'music_pause',
    icon: 'pause',
  },
  {
    key: 'music-play-pause',
    type: 'music_play_pause',
    icon: 'play-circle',
  },
  {
    key: 'music-now-playing',
    type: 'music_play_pause',
    icon: 'media-player',
  },
  {
    key: 'music-next',
    type: 'music_next',
    icon: 'next',
  },
  {
    key: 'music-previous',
    type: 'music_previous',
    icon: 'previous',
  },
  {
    key: 'music-set-volume',
    type: 'music_set_volume',
    icon: 'volume-two',
    defaults: { volume: 50 },
  },
  {
    key: 'music-volume-up',
    type: 'music_volume_up',
    icon: 'volume-three',
    defaults: { step: 10 },
  },
  {
    key: 'music-volume-down',
    type: 'music_volume_down',
    icon: 'volume-one',
    defaults: { step: 10 },
  },
  {
    key: 'music-volume-mute',
    type: 'music_volume_mute',
    icon: 'volume-muted',
    defaults: { unmuteVolume: 50 },
  },
  {
    key: 'music-seek',
    type: 'music_seek',
    icon: 'forward',
    defaults: { positionSeconds: 0 },
  },
  {
    key: 'music-set-shuffle',
    type: 'music_set_shuffle',
    icon: 'shuffle',
    defaults: { enabled: true },
  },
  {
    key: 'music-toggle-shuffle',
    type: 'music_toggle_shuffle',
    icon: 'shuffle',
  },
  {
    key: 'music-set-repeat',
    type: 'music_set_repeat',
    icon: 'repeat',
    defaults: { mode: 'off' },
  },
  {
    key: 'music-cycle-repeat',
    type: 'music_cycle_repeat',
    icon: 'repeat-one',
  },
  {
    key: 'music-transfer-device',
    type: 'music_transfer_device',
    icon: 'devices',
  },
  {
    key: 'music-save-track',
    type: 'music_save_track',
    icon: 'heart',
  },
  {
    key: 'music-unsave-track',
    type: 'music_unsave_track',
    icon: 'heart-broken',
  },
  {
    key: 'music-toggle-saved',
    type: 'music_toggle_saved',
    icon: 'heart-filled',
  },
  {
    key: 'music-add-to-playlist',
    type: 'music_add_to_playlist',
    icon: 'playlist-add',
  },
  {
    key: 'music-remove-from-playlist',
    type: 'music_remove_from_playlist',
    icon: 'playlist-remove',
  },
  {
    key: 'music-follow-artist',
    type: 'music_follow_artist',
    icon: 'user-add',
  },
  {
    key: 'music-unfollow-artist',
    type: 'music_unfollow_artist',
    icon: 'user-remove',
  },
] as const satisfies readonly StreamDeckAction[];

// The plugin key a saved button came from: the same bot action and icon, else the first key for that action.
// Two keys share music_play_pause, so the icon tells them apart. Undefined for an action the plugin has no key for.
export function deckActionFor(type: string, icon: string): StreamDeckAction | undefined {
  const sameType = STREAM_DECK_MUSIC_ACTIONS.filter((action) =>
    action.type === type);

  return sameType.find((action) =>
    action.icon === icon) ?? sameType[0];
}
