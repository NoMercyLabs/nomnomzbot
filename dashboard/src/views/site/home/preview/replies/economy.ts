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

import type { FakeReply } from '../fakeApi';
import economyConfig from '../fixtures/economy-config.json';
import economyRules from '../fixtures/economy-earning-rules.json';
import { channelPath, fixture, get, list, minutesAgo, one, sampleChannelId } from './shape';

const economy = `${channelPath}/economy`;
const stamp = {
  createdAt: minutesAgo(60 * 24 * 90),
  updatedAt: minutesAgo(60 * 24 * 3),
};

// A fresh install has the currency switched off and called "points". The sample channel switched it on and named
// it; every other field, and every earning rule with its default rate, is the server's own.
const config = {
  ...fixture(economyConfig),
  data: {
    ...fixture(economyConfig).data,
    currencyName: 'Crumb',
    currencyNamePlural: 'Crumbs',
    isEnabled: true,
  },
};

const earning = new Set<string>(['ChatMessage', 'Follow', 'Subscription', 'GiftSubscription', 'Raid']);

const earningRules = {
  ...fixture(economyRules),
  data: fixture(economyRules).data.map((rule) => ({
    ...rule,
    isEnabled: earning.has(rule.source),
  })),
};

type Item = {
  name: string;
  about: string;
  cost: number;
  cooldown?: number;
  stock?: number;
};

// Store items are the streamer's own content, so they are sample. A store item runs a pipeline (sink type
// "pipeline"), the sink the server runs for event responses and commands.
const items: Item[] = [
  {
    name: 'Hydrate!',
    about: 'The streamer takes a sip of water, on stream.',
    cost: 150,
    cooldown: 120,
  },
  {
    name: 'Pick the next song',
    about: 'Skips to a song of your choice in the queue.',
    cost: 400,
    cooldown: 300,
  },
  {
    name: 'Name on the wall',
    about: 'Your name on the end credits of the next stream.',
    cost: 1500,
    stock: 5,
  },
];

const catalog = items.map((item, index) => ({
  id: `sample-item-${index}`,
  name: item.name,
  description: item.about,
  sinkType: 'pipeline',
  cost: item.cost,
  iconUrl: null,
  isEnabled: true,
  permission: 'Everyone',
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
  get(`${economy}/config`, config),
  get(`${economy}/earning-rules`, earningRules),
  get(`${economy}/catalog`, list(catalog)),
  get(`${economy}/jars`, one(jars)),
  get(`${economy}/accounts`, one(accounts)),
  get(`${economy}/leaderboards/configs`, one(leaderboards)),
];
