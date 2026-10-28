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

// The domain word lists the bot and the dashboard share. A list lives here once; a view, a draft or a panel
// imports it and never spells it again (src/__tests__/structure.spec.ts guards it). The server sends these as
// plain strings, so the schema holds no enum to derive them from. The role ladder with its numbers is
// `rungs` in ./permissions; the labels of every word are i18n keys, never text in code.

// The platforms a channel streams on. The dashboard manages them side by side.
export const streamPlatforms = ['twitch', 'kick', 'youtube'] as const;

export type StreamPlatform = (typeof streamPlatforms)[number];

// The scope set a platform other than Twitch needs to manage a stream (server OAuthProviderRegistry).
// Twitch signs in with the account itself and has no set.
export const platformScopeSets = {
  kick: 'kick.chat',
  youtube: 'youtube.manage',
} as const satisfies Partial<Record<StreamPlatform, string>>;

// Where an emote comes from, in the order the dashboard lists them.
export const emoteProviders = ['Twitch', 'SevenTv', 'Bttv', 'Ffz'] as const;

export type EmoteProvider = (typeof emoteProviders)[number];

// The services a song request can play from. "auto" lets the bot pick (MusicService).
export const musicProviders = ['auto', 'spotify', 'youtube'] as const;

export type MusicProvider = (typeof musicProviders)[number];

// The rungs the bot knows for a per-role song cap, in the order they rank (MusicService role map).
export const musicRoleRungs = ['viewer', 'subscriber', 'vip', 'moderator', 'broadcaster'] as const;

export type MusicRoleRung = (typeof musicRoleRungs)[number];

// The bot's own trust tier names, in ladder order. "Regular" is one of them, earned in this channel;
// it has nothing to do with Twitch's account type.
export const trustTiers = [
  'Untrusted',
  'Newcomer',
  'Known',
  'Regular',
  'SemiTrusted',
  'Trusted',
  'Established',
] as const;

export type TrustTier = (typeof trustTiers)[number];

// The standings a game can be limited to, in ladder order.
export const gameStandings = ['Everyone', 'Subscriber', 'Vip', 'Artist', 'Moderator'] as const;

export type GameStanding = (typeof gameStandings)[number];
