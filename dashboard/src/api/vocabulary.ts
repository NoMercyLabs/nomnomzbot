// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

// The values the bot accepts for a song request's minimum trust level (MusicService.ParseMinTrustLevel);
// anything else means everyone.
export const musicTrustLevels = [
  'everyone',
  'subscribers',
  'vip',
  'moderators',
  'broadcaster',
] as const;

export type MusicTrustLevel = (typeof musicTrustLevels)[number];

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
