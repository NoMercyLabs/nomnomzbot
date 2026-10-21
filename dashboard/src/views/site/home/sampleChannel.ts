// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Every number and name the public home page shows. All of it is invented sample data for a made-up
// channel; none of it is read from a real channel, and the page says so next to the previews.
import type { StatusTone } from '@/components/layout/StatusBadge.vue';

export type SampleRow = {
  id: string;
  who?: string;
  value?: number;
  tone?: StatusTone;
  platform?: string;
};

export const sampleChannel = {
  handle: 'crumb_tv',
  viewers: 1204,
  followers: 38910,
  chatters: 612,
  uptimeMinutes: 134,
  raiders: 214,
  resubMonths: 6,
} as const;

export const featureGroups = [
  'chat',
  'pipelines',
  'moderation',
  'loyalty',
  'music',
  'overlays',
  'analytics',
  'platforms',
] as const;

export type FeatureGroup = (typeof featureGroups)[number];

export const sampleViewers = {
  songRequest: 'daisy_plays',
  raidMod: 'mod_kris',
  socials: 'viewer_87',
  uptime: 'viewer_42',
  reported: 'viewer_204',
  unban: 'nite_owl22',
  offTopic: 'chat_fan99',
} as const;

export const sampleRows = {
  chat: [
    { id: 'request', who: sampleViewers.songRequest },
    { id: 'reply' },
    { id: 'uptime', who: sampleViewers.uptime },
    { id: 'uptimeReply' },
  ],
  pipelines: [{ id: 'trigger' }, { id: 'check' }, { id: 'chat' }, { id: 'overlay' }],
  moderation: [
    {
      id: 'spam',
      who: sampleViewers.reported,
      tone: 'bad',
    },
    {
      id: 'unban',
      who: sampleViewers.unban,
      tone: 'live',
    },
    {
      id: 'offTopic',
      who: sampleViewers.offTopic,
      tone: 'off',
    },
  ],
  loyalty: [
    {
      id: 'first',
      who: sampleViewers.reported,
      value: 4210,
    },
    {
      id: 'second',
      who: sampleViewers.songRequest,
      value: 3180,
    },
    {
      id: 'third',
      who: sampleViewers.socials,
      value: 2455,
    },
    {
      id: 'fourth',
      who: sampleViewers.unban,
      value: 960,
    },
  ],
  music: [
    { id: 'now', tone: 'ok' },
    {
      id: 'queued',
      who: sampleViewers.songRequest,
      tone: 'live',
    },
    {
      id: 'tts',
      who: sampleViewers.socials,
      tone: 'ok',
    },
  ],
  overlays: [
    { id: 'alerts', tone: 'ok' },
    { id: 'chat', tone: 'ok' },
    { id: 'goal', tone: 'off' },
  ],
  analytics: [
    { id: 'viewers', value: sampleChannel.viewers },
    { id: 'followers', value: sampleChannel.followers },
    { id: 'chatters', value: sampleChannel.chatters },
  ],
  platforms: [
    {
      id: 'twitch',
      platform: 'twitch',
      tone: 'ok',
    },
    {
      id: 'kick',
      platform: 'kick',
      tone: 'ok',
    },
    {
      id: 'youtube',
      platform: 'youtube',
      tone: 'ok',
    },
    {
      id: 'x',
      platform: 'x',
      tone: 'off',
    },
  ],
} as const satisfies Record<FeatureGroup, readonly SampleRow[]>;
