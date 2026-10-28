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

import { QueryClient } from '@tanstack/vue-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { chatEmotesOptions, chatKey, type ChatEmoteEntry, type ChatLine } from '@/api/chat';

import type { RedemptionActivityPayload, RewardRedeemedPayload } from '../payloads';
import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

type Hub = typeof import('../dashboardHub');

const reward: RewardRedeemedPayload = {
  broadcasterId: 'A',
  rewardId: 'r1',
  rewardTitle: 'Hydrate',
  redemptionId: 'red1',
  userId: 'u1',
  userDisplayName: 'Stoney',
  cost: 100,
  userInput: 'drink water',
  timestamp: '2026-10-15T20:00:00Z',
};

const emoteUrls = { '1x': 'https://cdn.example/kappa/1x.webp' } as const satisfies Record<string, string>;

const kappa = {
  fragments: [
    {
      type: 'text',
      text: 'drink ',
      emote: null,
      cheermote: null,
      mention: null,
      linkPreview: null,
    },
    {
      type: 'emote',
      text: 'Kappa',
      emote: {
        id: '25',
        format: 'static',
        provider: 'Twitch',
        urls: emoteUrls,
        animated: false,
        zeroWidth: false,
        code: 'Kappa',
      },
      cheermote: null,
      mention: null,
      linkPreview: null,
    },
  ],
} as const satisfies Pick<ChatLine, 'fragments'>;

const kappaEntry = {
  code: 'Kappa',
  id: '25',
  provider: 'Twitch',
  urls: emoteUrls,
  animated: false,
  zeroWidth: false,
} as const satisfies ChatEmoteEntry;

const chatLine = (userInput: string): ChatLine => ({
  ...rewardLineBase,
  id: 'm1',
  message: userInput,
  messageType: 'text',
  redemption: undefined,
  ...kappa,
});

const activity = {
  broadcasterId: 'A',
  kind: 'custom_power_up',
  redemptionId: 'pu1',
  title: 'Confetti',
  userId: 'u2',
  userLogin: 'fan',
  userDisplayName: 'Fan',
  cost: 50,
  costUnit: 'bits',
  userInput: null,
  timestamp: '2026-10-15T20:01:00Z',
} as const satisfies RedemptionActivityPayload;

async function started(): Promise<[Hub, QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub');
  const queries = new QueryClient();

  await hub.startDashboardHub(queries);
  await hub.watchChannel('A');
  queries.setQueryData<ChatLine[]>(chatKey('A'), []);

  return [hub, queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)];
}

const lines = (queries: QueryClient): ChatLine[] | undefined =>
  queries.getQueryData<ChatLine[]>(chatKey('A'));

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

const rewardLineBase: ChatLine = {
  id: '',
  channelId: 'A',
  userId: 'u1',
  displayName: 'Stoney',
  username: 'Stoney',
  message: '',
  fragments: [],
  userType: 'viewer',
  isSubscriber: false,
  isVip: false,
  isArtist: false,
  isModerator: false,
  isBroadcaster: false,
  isCheer: false,
  isCommand: false,
  badges: [],
  bitsAmount: 0,
  color: null,
  messageType: 'text',
  replyToMessageId: null,
  replyParentMessageBody: null,
  replyParentUserName: null,
  timestamp: '2026-10-15T20:00:01Z',
  provider: 'twitch',
};

describe('redemptions in chat', () => {
  it('a custom reward redemption becomes a chat line with the reward title, cost and viewer input', async () => {
    const [, queries, connection] = await started();

    connection.receive('RewardRedeemed', reward);

    expect(lines(queries)).toMatchObject([
      {
        id: 'redemption:red1',
        userId: 'u1',
        displayName: 'Stoney',
        message: 'drink water',
        timestamp: '2026-10-15T20:00:00Z',
        redemption: {
          kind: 'reward',
          title: 'Hydrate',
          cost: 100,
          costUnit: 'channel_points',
        },
      },
    ]);
  });

  it('a Power-up redemption becomes a chat line priced in Bits', async () => {
    const [, queries, connection] = await started();

    connection.receive('RedemptionActivity', activity);

    expect(lines(queries)).toMatchObject([
      {
        id: 'redemption:pu1',
        username: 'fan',
        message: '',
        redemption: {
          kind: 'custom_power_up',
          title: 'Confetti',
          cost: 50,
          costUnit: 'bits',
        },
      },
    ]);
  });

  it('the same redemption twice makes one line', async () => {
    const [, queries, connection] = await started();

    connection.receive('RedemptionActivity', activity);
    connection.receive('RedemptionActivity', activity);

    expect(lines(queries)).toHaveLength(1);
  });

  it('the input takes the fragments of the chat line the same viewer sent with it', async () => {
    const [, queries, connection] = await started();

    connection.receive('ChatMessage', chatLine('drink Kappa'));
    connection.receive('RewardRedeemed', { ...reward, userInput: 'drink Kappa' });

    const found = lines(queries)?.find((line) =>
      line.id === 'redemption:red1');

    expect(found?.fragments).toEqual(kappa.fragments);
  });

  it('a chat line that arrives after the redemption fills its fragments in', async () => {
    const [, queries, connection] = await started();

    connection.receive('RewardRedeemed', { ...reward, userInput: 'drink Kappa' });
    connection.receive('ChatMessage', chatLine('drink Kappa'));

    const found = lines(queries)?.find((line) =>
      line.id === 'redemption:red1');

    expect(found?.fragments).toEqual(kappa.fragments);
  });

  it('a chat line with other words or another viewer is not the same redemption', async () => {
    const [, queries, connection] = await started();

    connection.receive('ChatMessage', { ...chatLine('drink Kappa'), userId: 'u9' });
    connection.receive('ChatMessage', { ...chatLine('hello'), id: 'm2' });
    connection.receive('RewardRedeemed', { ...reward, userInput: 'drink Kappa' });

    const found = lines(queries)?.find((line) =>
      line.id === 'redemption:red1');

    expect(found?.fragments.filter((fragment) =>
      fragment.emote)).toEqual([]);
  });

  it('with no chat line, known emote names from the loaded emote sets become emotes and other words stay text', async () => {
    const [, queries, connection] = await started();

    queries.setQueryData<ChatEmoteEntry[]>(chatEmotesOptions('A').queryKey, [kappaEntry]);
    connection.receive('RewardRedeemed', { ...reward, userInput: 'drink Kappa Nope' });

    const found = lines(queries)?.find((line) =>
      line.id === 'redemption:red1');

    expect(found?.fragments.map((fragment) =>
      [fragment.type, fragment.text, fragment.emote?.id ?? null])).toEqual([
      ['text', 'drink ', null],
      ['emote', 'Kappa', '25'],
      ['text', ' Nope', null],
    ]);
  });
});
