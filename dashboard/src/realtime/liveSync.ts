// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { QueryClient } from '@tanstack/vue-query';

import { currentUserQuery } from '@/api/auth';
import { communityFirstPageKey } from '@/api/community';
import { activityKey, botStatusKey, connectionsKey, homeStatsKey, reviewQueueKey } from '@/api/home';
import { pollsKey, predictionsKey } from '@/api/liveOps';
import { musicQueueKey } from '@/api/music';
import { communityListsKey, viewerProfilesKey } from '@/api/people';
import { rewardsKey } from '@/api/rewards';
import { adScheduleKey } from '@/api/stream';
import { merge } from '@/lib/array';

import { channelPrefixes, configDomainKeys } from './configDomains';
import type { HubEventHandler, QueryKeyBuilder } from './hubEvent';
import type { ConfigChangedPayload } from './payloads';

function invalidateKeys(
  queries: QueryClient,
  keys: readonly QueryKeyBuilder[],
  channelId: string | null,
): void {
  for (const key of keys) {
    void queries.invalidateQueries({ queryKey: key(channelId) });
  }
}

// An event that only marks page queries of the joined channel stale; the page loads them again if it shows them.
function refreshing(event: string, keys: readonly QueryKeyBuilder[]): HubEventHandler<unknown> {
  return {
    event,
    handle: ({ queries, channelId }) => {
      invalidateKeys(queries, keys, channelId);
    },
  };
}

const homeKeys = [homeStatsKey, activityKey] as const;

// A hub connection can hold several handlers for one event. These add the home and review refreshes to the
// events that channels.ts, community.ts and chat.ts already handle for their own data.
const eventHandlers = [
  refreshing('ChannelEvent', homeKeys),
  refreshing('CommandExecuted', homeKeys),
  refreshing('ModAction', merge(homeKeys, [reviewQueueKey])),
  refreshing('StreamStatusChanged', merge(homeKeys, [botStatusKey, adScheduleKey])),
  refreshing('StreamInfoChanged', [homeStatsKey]),
  refreshing('MusicStateChanged', [musicQueueKey]),
  refreshing('RewardRedeemed', merge(homeKeys, [rewardsKey])),
  refreshing('RedemptionStatusChanged', merge(homeKeys, [rewardsKey])),
  refreshing('RewardChanged', [rewardsKey]),
  refreshing('AlertTriggered', merge(homeKeys, [reviewQueueKey, pollsKey, predictionsKey, adScheduleKey])),
  refreshing('ObsBridgeStateChanged', [connectionsKey, botStatusKey]),
  refreshing('ObsLiveStateChanged', [connectionsKey, botStatusKey]),
] as const;

const permissionChanged = {
  event: 'PermissionChanged',
  handle: ({ queries, channelId }) => {
    const keys = [communityListsKey, viewerProfilesKey, communityFirstPageKey];

    invalidateKeys(queries, keys, channelId);
    void queries.invalidateQueries({ queryKey: currentUserQuery.queryKey });
  },
} as const satisfies HubEventHandler<unknown>;

const configChanged = {
  event: 'ConfigChanged',
  handle: ({ queries, channelId }, change) => {
    const keys = configDomainKeys[change.domain] ?? channelPrefixes;

    invalidateKeys(queries, keys, channelId);
  },
} as const satisfies HubEventHandler<ConfigChangedPayload>;

export const liveHandlers = merge(eventHandlers, [permissionChanged, configChanged]);
