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

import { actionRequiredKey } from '@/api/actionRequired';
import { activityKey, botStatusKey, connectionsKey, homeStatsKey, reviewQueueKey } from '@/api/home';
import { giveawaysKey } from '@/api/giveaways';
import { pollHistoryKey, pollsKey, predictionsKey, timersKey } from '@/api/liveOps';
import {
  musicConfigKey,
  musicHistoryKey,
  musicQueueKey,
  musicTopRequestersKey,
} from '@/api/music';
import { currencyConfigKey, earningRulesKey, savingsJarsKey } from '@/api/economy';
import { communityListsKey, viewerProfilesKey } from '@/api/people';
import { pipelinesKey } from '@/api/pipelines';
import { redemptionsKey, rewardsKey, type Redemption } from '@/api/rewards';
import { adScheduleKey } from '@/api/stream';

import { FakeHubConnection, fakeSignalR } from './fakeSignalR';

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

type Keys = (channelId: string | null) => unknown[];

// The same key api/eventResponses.ts builds; that module is not imported so this spec stands alone.
const eventResponsesKey: Keys = (channelId) => ['channels', channelId, 'event-responses'];

async function started(): Promise<[QueryClient, FakeHubConnection]> {
  const hub = await import('../dashboardHub');
  const queries = new QueryClient();

  await hub.startDashboardHub(queries);
  await hub.watchChannel('c1');

  return [queries, FakeHubConnection.at(FakeHubConnection.instances.length - 1)];
}

function seed(queries: QueryClient, keys: Keys[]): void {
  for (const key of keys) {
    queries.setQueryData(key('c1'), []);
    queries.setQueryData(key('other'), []);
  }
}

function staleFor(queries: QueryClient, keys: Keys[], channelId: string): boolean[] {
  return keys.map((key) => queries.getQueryState(key(channelId))?.isInvalidated === true);
}

beforeEach(() => {
  vi.resetModules();
  FakeHubConnection.instances = [];
});

const everyKey: Keys[] = [
  homeStatsKey,
  activityKey,
  reviewQueueKey,
  connectionsKey,
  botStatusKey,
  timersKey,
  pollsKey,
  predictionsKey,
  pollHistoryKey,
  adScheduleKey,
  musicQueueKey,
  musicHistoryKey,
  musicTopRequestersKey,
  musicConfigKey,
  rewardsKey,
  eventResponsesKey,
  pipelinesKey,
  actionRequiredKey,
  communityListsKey,
  currencyConfigKey,
  earningRulesKey,
  savingsJarsKey,
];
  giveawaysKey,
  vtsConnectionKey,
  vtsInventoryKey,

// Each hub event with the keys it must mark stale for the joined channel.
const eventKeys: [string, unknown, Keys[]][] = [
  [
    'MusicStateChanged',
    { isPlaying: true, currentTrack: null },
    [musicQueueKey, musicHistoryKey, musicTopRequestersKey],
  ],
  // Owner 2026-10-14: "i queued a song and it took too long to show up, why is this not socket based?"
  ['sr_queue_changed', { items: [] }, [musicQueueKey, musicHistoryKey, musicTopRequestersKey]],
  ['RewardRedeemed', { broadcasterId: 'x' }, [homeStatsKey, activityKey]],
  ['RedemptionStatusChanged', { broadcasterId: 'x' }, [homeStatsKey, activityKey]],
  ['RewardChanged', { broadcasterId: 'x', action: 'updated' }, [rewardsKey]],
  ['AlertTriggered', {
    type: 'poll_begin',
    message: null,
    data: null,
  }, [
    homeStatsKey,
    activityKey,
    reviewQueueKey,
    pollsKey,
    predictionsKey,
    pollHistoryKey,
    adScheduleKey,
  ]],
  ['PermissionChanged', {
    subjectType: 'user',
    subjectId: 'u',
    resourceType: 'x',
    resourceId: 'y',
    value: 1,
  }, [
    communityListsKey,
  ]],
  ['ObsBridgeStateChanged', {
    broadcasterId: 'x',
    instanceCount: 1,
    hasLeader: true,
  }, [
    connectionsKey,
    botStatusKey,
  ]],
  ['ObsLiveStateChanged', { broadcasterId: 'x' }, [connectionsKey, botStatusKey]],
  // Owner 2026-10-15: "ALL data must be live updates as it happens". The server sends each poll,
  // prediction and chat poll change as a ChannelEvent; the history list follows every one of them.
  ...['poll_begin', 'poll_progress', 'poll_end'].map((type): [string, unknown, Keys[]] => [
    'ChannelEvent',
    {
      type,
      broadcasterId: 'x',
      data: null,
    },
    [homeStatsKey, activityKey, pollsKey, pollHistoryKey],
  ]),
  ...['prediction_begin', 'prediction_progress', 'prediction_lock', 'prediction_end'].map(
    (type): [string, unknown, Keys[]] => [
      'ChannelEvent',
      {
        type,
        broadcasterId: 'x',
        data: null,
      },
      [homeStatsKey, activityKey, predictionsKey, pollHistoryKey],
    ],
  ),
  ['ChannelEvent', {
    type: 'ChatPollChanged',
    broadcasterId: 'x',
    data: null,
  }, [homeStatsKey, activityKey, pollHistoryKey]],
  ['ChannelEvent', {
    type: 'follow',
    broadcasterId: 'x',
    data: null,
  }, [homeStatsKey, activityKey]],
  ['CommandExecuted', {
    broadcasterId: 'x',
  ['ChannelEvent', {
    type: 'GiveawayChanged',
    broadcasterId: 'x',
    data: { giveawayId: 'g1', change: 'drawn' },
  }, [homeStatsKey, activityKey, giveawaysKey]],
    commandName: 'hug',
    succeeded: true,
  }, [
    homeStatsKey,
    activityKey,
  ]],
  ['ModAction', {
    action: 'ban',
    targetUserId: 't',
    broadcasterId: 'x',
  }, [
    homeStatsKey,
    activityKey,
    reviewQueueKey,
    communityListsKey,
  ]],
  ['StreamStatusChanged', {
    isLive: true,
    streamId: 's',
    title: null,
    gameName: null,
    startedAt: null,
  }, [
    homeStatsKey,
    activityKey,
    botStatusKey,
    adScheduleKey,
  ]],
  ['StreamInfoChanged', {
    broadcasterId: 'x',
    title: 't',
    gameName: 'g',
  }, [homeStatsKey]],
];

describe('live events refresh the data they change', () => {
  it.each(eventKeys)('%s marks its keys stale for the joined channel only', async (event, payload, keys) => {
    const [queries, connection] = await started();

    seed(queries, everyKey);
    connection.receive(event, payload);

    expect(staleFor(queries, keys, 'c1')).toEqual(keys.map(() => true));
    expect(staleFor(queries, keys, 'other')).toEqual(keys.map(() => false));

    const untouched = everyKey.filter((key) => !keys.includes(key));

    expect(staleFor(queries, untouched, 'c1')).toEqual(untouched.map(() => false));
  });
});

describe('the redemption queue follows the hub without a refetch', () => {
  const row = (redemptionId: string, status: string): Redemption => ({
    redemptionId,
    rewardId: 'r1',
    rewardTitle: 'Hydrate',
    userId: 'u1',
    userDisplayName: 'Viewer',
    cost: 500,
    status,
    redeemedAt: '2026-10-15T12:00:00Z',
  });

  it('puts a new redemption at the top as waiting, once', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(redemptionsKey('c1'), [row('d1', 'unfulfilled')]);

    const payload = {
      broadcasterId: 'x',
      rewardId: 'r1',
      rewardTitle: 'Hydrate',
      redemptionId: 'd2',
      userId: 'u2',
      userDisplayName: 'Other',
      cost: 500,
      userInput: 'hello',
      timestamp: '2026-10-15T12:05:00Z',
    };

    connection.receive('RewardRedeemed', payload);
    connection.receive('RewardRedeemed', payload);

    const rows = queries.getQueryData<Redemption[]>(redemptionsKey('c1')) ?? [];

    expect(rows.map((entry) => [entry.redemptionId, entry.status, entry.userInput])).toEqual([
      ['d2', 'unfulfilled', 'hello'],
      ['d1', 'unfulfilled', undefined],
    ]);

    expect(queries.getQueryState(redemptionsKey('c1'))?.isInvalidated).toBe(false);
  });

  it('rewrites the status of the row Twitch changed', async () => {
    const [queries, connection] = await started();

    queries.setQueryData(redemptionsKey('c1'), [row('d1', 'unfulfilled'), row('d2', 'unfulfilled')]);

    connection.receive('RedemptionStatusChanged', {
      broadcasterId: 'x',
      redemptionId: 'd1',
      rewardId: 'r1',
      status: 'CANCELED',
      timestamp: '2026-10-15T12:06:00Z',
    });

    const rows = queries.getQueryData<Redemption[]>(redemptionsKey('c1')) ?? [];

    expect(rows.map((entry) => [entry.redemptionId, entry.status])).toEqual([
      ['d1', 'canceled'],
      ['d2', 'unfulfilled'],
    ]);
  });
});

// Each domain that a server service publishes with ConfigChanged.
const domainKeys: [string, Keys[]][] = [
  ['timers', [timersKey]],
  ['event-responses', [eventResponsesKey]],
  ['pipelines', [pipelinesKey]],
  ['music-config', [musicConfigKey]],
  ['sr-config', [musicConfigKey]],
  ['channel-settings', [botStatusKey, connectionsKey]],
  ['features', [botStatusKey]],
  ['webhooks', [connectionsKey]],
  ['moderation-history', [reviewQueueKey, communityListsKey]],
  ['suspicious-users', [reviewQueueKey, communityListsKey]],
  ['unban-requests', [reviewQueueKey, communityListsKey]],
  ['viewer-reports', [reviewQueueKey, communityListsKey]],
  ['raid-assessments', [reviewQueueKey, communityListsKey]],
  ['economy-config', [currencyConfigKey]],
  ['earning-rules', [earningRulesKey]],
  ['savings-jar', [savingsJarsKey]],
];

describe('a saved or removed shoutout message refreshes the open profiles', () => {
  it.each(['updated', 'deleted'])('the shoutouts domain (%s) marks the profiles of the joined channel stale only', async (action) => {
    const [queries, connection] = await started();

    seed(queries, [viewerProfilesKey, timersKey]);

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain: 'shoutouts',
      entityId: null,
      action,
    });

    expect(staleFor(queries, [viewerProfilesKey], 'c1')).toEqual([true]);
    expect(staleFor(queries, [viewerProfilesKey], 'other')).toEqual([false]);
    expect(staleFor(queries, [timersKey], 'c1')).toEqual([false]);
  });
});

describe('every ConfigChanged domain refreshes its page', () => {
  it.each(domainKeys)('the %s domain marks its keys stale for the joined channel only', async (domain, keys) => {
    const [queries, connection] = await started();

    seed(queries, everyKey);

    connection.receive('ConfigChanged', {
      broadcasterId: 'x',
      domain,
      entityId: null,
      action: 'updated',
    });

    expect(staleFor(queries, keys, 'c1')).toEqual(keys.map(() => true));
    expect(staleFor(queries, keys, 'other')).toEqual(keys.map(() => false));
  });

  it.each(['catalog', 'quotes', 'picklists'])(
    'the %s domain has no page key of its own and marks every query of the joined channel stale',
    async (domain) => {
      const [queries, connection] = await started();

      seed(queries, everyKey);

      connection.receive('ConfigChanged', {
        broadcasterId: 'x',
        domain,
        entityId: null,
        action: 'updated',
      });

      expect(staleFor(queries, everyKey, 'c1').every(Boolean)).toBe(true);
      expect(staleFor(queries, everyKey, 'other').some(Boolean)).toBe(false);
    },
  );
});
