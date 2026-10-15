// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient } from '@tanstack/vue-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { chatKey, type ChatLine } from '@/api/chat';

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

const activity: RedemptionActivityPayload = {
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
};

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
        redemption: { kind: 'reward', title: 'Hydrate', cost: 100, costUnit: 'channel_points' },
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
        redemption: { kind: 'custom_power_up', title: 'Confetti', cost: 50, costUnit: 'bits' },
      },
    ]);
  });

  it('the same redemption twice makes one line', async () => {
    const [, queries, connection] = await started();

    connection.receive('RedemptionActivity', activity);
    connection.receive('RedemptionActivity', activity);

    expect(lines(queries)).toHaveLength(1);
  });
});
