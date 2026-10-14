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
import { pollsKey, predictionsKey, timersKey } from '@/api/liveOps';
import { musicConfigKey, musicQueueKey } from '@/api/music';
import { communityListsKey } from '@/api/people';
import { pipelinesKey } from '@/api/pipelines';
import { rewardsKey } from '@/api/rewards';
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
  adScheduleKey,
  musicQueueKey,
  musicConfigKey,
  rewardsKey,
  eventResponsesKey,
  pipelinesKey,
  actionRequiredKey,
  communityListsKey,
];

// Each hub event with the keys it must mark stale for the joined channel.
const eventKeys: [string, unknown, Keys[]][] = [
  ['MusicStateChanged', { isPlaying: true, currentTrack: null }, [musicQueueKey]],
  ['RewardRedeemed', { broadcasterId: 'x' }, [homeStatsKey, activityKey, rewardsKey]],
  ['RedemptionStatusChanged', { broadcasterId: 'x' }, [homeStatsKey, activityKey, rewardsKey]],
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
  ['ChannelEvent', {
    type: 'follow',
    broadcasterId: 'x',
    data: null,
  }, [homeStatsKey, activityKey]],
  ['CommandExecuted', {
    broadcasterId: 'x',
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
];

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

  it.each(['catalog', 'earning-rules', 'economy-config', 'quotes', 'savings-jar', 'picklists'])(
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
