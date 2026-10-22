// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { FakeReply } from '../fakeApi';
import { channelPath, get, list, minutesAgo, one, sampleChannelId } from './shape';

const economy = `${channelPath}/economy`;
const stamp = {
  createdAt: minutesAgo(60 * 24 * 90),
  updatedAt: minutesAgo(60 * 24 * 3),
};

const config = {
  id: 'sample-currency',
  broadcasterId: sampleChannelId,
  currencyName: 'Crumb',
  currencyNamePlural: 'Crumbs',
  iconUrl: null,
  isEnabled: true,
  startingBalance: 100,
  maxBalance: 1_000_000,
  decimalPlaces: 0,
  ...stamp,
};

type Rule = {
  source: string;
  rate: number;
  window?: number;
  cap?: number;
  enabled?: boolean;
};

const rules: Rule[] = [
  {
    source: 'ChatMessage',
    rate: 2,
    window: 60,
    cap: 50,
  },
  {
    source: 'WatchTime',
    rate: 10,
    window: 300,
  },
  { source: 'Follow', rate: 250 },
  { source: 'Subscription', rate: 1000 },
  { source: 'GiftSubscription', rate: 500 },
  {
    source: 'Cheer',
    rate: 1,
    enabled: false,
  },
  { source: 'Raid', rate: 5 },
];

const earningRules = rules.map((rule, index) => ({
  id: `sample-rule-${index}`,
  source: rule.source,
  isEnabled: rule.enabled ?? true,
  rate: rule.rate,
  unitWindowSeconds: rule.window ?? null,
  perWindowCap: null,
  perStreamCap: rule.cap ?? null,
  minRoleLevel: null,
  configSchemaVersion: 1,
  bonusConfig: null,
}));

type Item = {
  name: string;
  about: string;
  sink: string;
  cost: number;
  cooldown?: number;
  stock?: number;
};

const items: Item[] = [
  {
    name: 'Hydrate!',
    about: 'The streamer takes a sip of water, on stream.',
    sink: 'pipeline',
    cost: 150,
    cooldown: 120,
  },
  {
    name: 'Pick the next song',
    about: 'Skips to a song of your choice in the queue.',
    sink: 'pipeline',
    cost: 400,
    cooldown: 300,
  },
  {
    name: 'Text to speech line',
    about: 'Your message, read out loud in the stream.',
    sink: 'tts',
    cost: 250,
    cooldown: 60,
  },
  {
    name: 'Sound clip',
    about: 'Plays a short clip from the sound board.',
    sink: 'sound',
    cost: 200,
    cooldown: 90,
  },
  {
    name: 'VIP for a month',
    about: 'A VIP badge for 30 days.',
    sink: 'role',
    cost: 25_000,
    stock: 5,
  },
  {
    name: 'Name on the wall',
    about: 'Your name on the end credits of the next stream.',
    sink: 'pipeline',
    cost: 1500,
  },
];

const catalog = items.map((item, index) => ({
  id: `sample-item-${index}`,
  name: item.name,
  description: item.about,
  sinkType: item.sink,
  cost: item.cost,
  iconUrl: null,
  isEnabled: true,
  permission: 'everyone',
  pipelineId: null,
  target: null,
  cooldownSeconds: item.cooldown ?? 0,
  cooldownPerUser: true,
  stockLimit: item.stock ?? null,
  stockRemaining: item.stock === undefined
    ? null
    : item.stock - 2,
  maxPerViewerPerStream: null,
  sortOrder: index,
  ...stamp,
}));

const jars = [
  {
    id: 'sample-jar-0',
    ownerBroadcasterId: sampleChannelId,
    name: 'Charity stream',
    description: 'Every Crumb in the jar goes to the stream goal.',
    goalAmount: 100_000,
    balance: 64_300,
    iconUrl: null,
    isOpen: true,
    maxWithdrawalPerChannel: null,
    ...stamp,
  },
  {
    id: 'sample-jar-1',
    ownerBroadcasterId: sampleChannelId,
    name: 'New microphone',
    description: null,
    goalAmount: 30_000,
    balance: 30_000,
    iconUrl: null,
    isOpen: false,
    maxWithdrawalPerChannel: null,
    ...stamp,
  },
];

const names = ['daisy_plays', 'nite_owl22', 'viewer_87', 'mod_kris', 'chat_fan99', 'viewer_204'];

const accounts = names.map((name, index) => ({
  id: `sample-account-${index}`,
  viewerUserId: `sample-viewer-${index}`,
  viewerTwitchUserId: `${1000 + index}`,
  viewerDisplayName: name,
  viewerAvatarUrl: null,
  balance: 4210 - index * 610,
  lifetimeEarned: 9800 - index * 900,
  lifetimeSpent: 5590 - index * 290,
  isFrozen: false,
  lastActivityAt: minutesAgo(5 + index * 40),
}));

const leaderboards = [
  {
    metric: 'balance',
    scope: 'channel',
    period: 'alltime',
    top: 10,
  },
  {
    metric: 'earned',
    scope: 'channel',
    period: 'weekly',
    top: 5,
  },
  {
    metric: 'spent',
    scope: 'channel',
    period: 'monthly',
    top: 5,
  },
].map((board, index) => ({
  id: `sample-board-${index}`,
  broadcasterId: sampleChannelId,
  jarId: null,
  metric: board.metric,
  scope: board.scope,
  period: board.period,
  isPublic: true,
  topN: board.top,
  ...stamp,
}));

export const economyReplies: FakeReply[] = [
  get(`${economy}/config`, one(config)),
  get(`${economy}/earning-rules`, one(earningRules)),
  get(`${economy}/catalog`, list(catalog)),
  get(`${economy}/jars`, one(jars)),
  get(`${economy}/accounts`, one(accounts)),
  get(`${economy}/leaderboards/configs`, one(leaderboards)),
];
