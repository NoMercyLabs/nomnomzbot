// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { merge } from '@/lib/array';

import type { FakeReply } from '../fakeApi';
import musicConfig from '../fixtures/music-config.json';
import { channelPath, fixture, get, one, minutesAgo } from './shape';

const music = `${channelPath}/music`;
const minute = 60_000;

// Invented tracks and artists; no real recording is named.
const tracks = [
  {
    name: 'Paper Lanterns',
    artist: 'The Marlow Hours',
    minutes: 3.4,
    by: 'daisy_plays',
  },
  {
    name: 'Low Tide Radio',
    artist: 'Velvet Static',
    minutes: 4.1,
    by: 'nite_owl22',
  },
  {
    name: 'Neon Orchard',
    artist: 'Kora Vale',
    minutes: 2.9,
    by: 'viewer_87',
  },
  {
    name: 'Slow Orbit',
    artist: 'North of Eleven',
    minutes: 3.7,
    by: 'chat_fan99',
  },
] as const;

const queue = tracks.map((track, index) => ({
  position: index + 1,
  number: index + 1,
  trackName: track.name,
  artist: track.artist,
  imageUrl: null,
  durationMs: Math.round(track.minutes * minute),
  requestedBy: track.by,
  cost: 400,
  requesterId: `sample-viewer-${index}`,
  requesterColor: null,
  waitsForMusicPlayer: false,
}));

const nowPlaying = {
  trackName: 'Glass Harbor',
  artist: 'Juniper Lane',
  album: 'Small Hours',
  imageUrl: null,
  durationMs: Math.round(3.6 * minute),
  progressMs: Math.round(1.2 * minute),
  isPlaying: true,
  volume: 65,
  requestedBy: 'mod_kris',
  provider: 'spotify',
  shuffleState: false,
  repeatState: 'off',
  canSetShuffle: true,
  canSetRepeat: true,
  canSkipNext: true,
  canSkipPrevious: true,
  canSeek: true,
  canPause: true,
  canResume: true,
  requesterId: 'sample-viewer-9',
  requesterColor: null,
  trackUri: null,
  engine: null,
  itemId: null,
  title: null,
  artists: null,
  albumArtUrl: null,
  muted: false,
  state: 'playing',
};

// The request settings of a fresh install, as the server sends them: its own defaults.
const config = fixture(musicConfig);

const buttons = [
  {
    id: 'sample-button-skip',
    label: 'Skip',
    icon: 'next',
    actionType: 'skipSong',
  },
  {
    id: 'sample-button-pause',
    label: 'Pause',
    icon: 'pause',
    actionType: 'pauseSong',
  },
  {
    id: 'sample-button-like',
    label: 'Save song',
    icon: 'heart',
    actionType: 'saveSong',
  },
].map((button) => ({ ...button, parameters: {} }));

const history = merge(tracks, tracks).map((track, index) => ({
  trackName: track.name,
  artist: track.artist,
  imageUrl: null,
  provider: index % 3 === 0
    ? 'youtube'
    : 'spotify',
  requesterId: `sample-viewer-${index % 4}`,
  requesterName: track.by,
  requestedAt: minutesAgo(8 + index * 14),
  requesterColor: null,
}));

const requesters = tracks.map((track, index) => ({
  userId: `sample-viewer-${index}`,
  displayName: track.by,
  count: 48 - index * 11,
  color: null,
}));

export const musicReplies: FakeReply[] = [
  get(`${music}/queue`, one({
    nowPlaying,
    queue,
    requestsOpen: true,
    blockedReason: null,
  })),
  get(`${music}/config`, config),
  get(`${music}/config/defaults`, config),
  get(`${music}/blocked-tracks`, one([])),
  get(`${music}/player-buttons`, one(buttons)),
  get(`${music}/history`, one(history)),
  get(`${music}/top-requesters`, one(requesters)),
];
