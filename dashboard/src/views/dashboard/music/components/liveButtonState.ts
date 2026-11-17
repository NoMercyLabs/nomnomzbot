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

// The player buttons whose action has a live state show it, as the Stream Deck's live keys do
// (tools/streamdeck/music/src/actions: toggleSaved, toggleShuffle, cycleRepeat, volumeMute, playPause).
// pressed is the on/off state (undefined: the action is not an on/off switch), icon the icon that shows it.
import type { NowPlaying } from '@/api/music';

export type LiveState = {
  pressed?: boolean;
  icon: string;
};

type LiveRule = (track: NowPlaying) => LiveState;

const liveRules = {
  music_toggle_saved: (track) => {
    const saved = track.isSaved === true;

    return { pressed: saved, icon: saved
      ? 'heart-filled'
      : 'heart' };
  },
  music_toggle_shuffle: (track) => ({ pressed: track.shuffleState, icon: 'shuffle' }),
  music_cycle_repeat: (track) => ({
    pressed: track.repeatState !== 'off',
    icon: track.repeatState === 'track'
      ? 'repeat-one'
      : 'repeat',
  }),
  music_volume_mute: (track) => ({ pressed: track.muted, icon: track.muted
    ? 'volume-muted'
    : 'volume-two' }),
  music_play_pause: (track) => ({ icon: track.isPlaying
    ? 'pause'
    : 'play' }),
} as const satisfies Record<string, LiveRule>;

function isLive(actionType: string): actionType is keyof typeof liveRules {
  return actionType in liveRules;
}

// The live state of a button's action on the song now playing; null for an action with none, or with no song.
export function liveStateOf(actionType: string, track: NowPlaying | null): LiveState | null {
  if (track === null || !isLive(actionType))
    return null;

  return liveRules[actionType](track);
}
