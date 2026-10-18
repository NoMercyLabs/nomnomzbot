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
import {
  activityKey,
  botStatusKey,
  connectionsKey,
  homeStatsKey,
  reviewQueueKey,
} from '@/api/home';
import { eventFeedsKey } from '@/api/customEventFeeds';
import { federationOptInsKey } from '@/api/federation';
import { giveawaysKey } from '@/api/giveaways';
import { pollHistoryKey, pollsKey, predictionsKey } from '@/api/liveOps';
import { mediaShareQueueKey } from '@/api/mediaShare';
import { musicHistoryKey, musicQueueKey, musicTopRequestersKey } from '@/api/music';
import { obsBridgeKey, obsConnectionKey, obsScenesKey, obsStateKey } from '@/api/obs';
import { communityListsKey, viewerProfilesKey } from '@/api/people';
import { rewardsKey } from '@/api/rewards';
import { adScheduleKey } from '@/api/stream';
import { vtsConnectionKey, vtsInventoryKey } from '@/api/vts';
import { widgetsKey } from '@/api/widgets';
import { merge } from '@/lib/array';

import { channelPrefixes, configDomainKeys } from './configDomains';
import type { HubEventHandler, QueryKeyBuilder } from './hubEvent';
import { redemptionRedeemed, redemptionStatusChanged } from './redemptionQueue';
import type {
  ChannelEventPayload,
  ConfigChangedPayload,
  DashboardHubEvents,
  PermissionChangedPayload,
} from './payloads';

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
function refreshing<TEvent extends keyof DashboardHubEvents>(
  event: TEvent,
  keys: readonly QueryKeyBuilder[],
): HubEventHandler<DashboardHubEvents[TEvent]> {
  return {
    event,
    handle: ({ queries, channelId }) => {
      invalidateKeys(queries, keys, channelId);
    },
  };
}

const homeKeys = [homeStatsKey, activityKey] as const;
const musicKeys = [musicQueueKey, musicHistoryKey, musicTopRequestersKey] as const;
// An overlay's browser source opens and closes with the OBS bridge, so the widgets list (isAttached) follows it.
const obsKeys = [obsConnectionKey, obsBridgeKey, obsStateKey, obsScenesKey, widgetsKey] as const;

// A hub connection can hold several handlers for one event. These add the home and review refreshes to the
// events that channels.ts, community.ts and chat.ts already handle for their own data.
const eventHandlers = [
  refreshing('ChannelEvent', homeKeys),
  refreshing('CommandExecuted', homeKeys),
  refreshing('ModAction', merge(homeKeys, [reviewQueueKey])),
  refreshing('StreamStatusChanged', merge(homeKeys, [botStatusKey, adScheduleKey])),
  refreshing('StreamInfoChanged', [homeStatsKey]),
  refreshing('MusicStateChanged', musicKeys),
  refreshing('sr_queue_changed', musicKeys),
  // The redemption queue itself is written from the event (redemptionQueue.ts), so it is not refetched.
  refreshing('RewardRedeemed', homeKeys),
  refreshing('RedemptionStatusChanged', homeKeys),
  refreshing('RewardChanged', [rewardsKey]),
  refreshing(
    'AlertTriggered',
    merge(homeKeys, [reviewQueueKey, pollsKey, predictionsKey, pollHistoryKey, adScheduleKey]),
  ),
  refreshing('ObsBridgeStateChanged', merge([connectionsKey, botStatusKey], obsKeys)),
  refreshing('ObsLiveStateChanged', merge([connectionsKey, botStatusKey], obsKeys)),
  refreshing('CustomDataSourceChanged', [eventFeedsKey]),
  refreshing('FederationOptInChanged', [federationOptInsKey]),
  refreshing('VtsStatusChanged', [connectionsKey, botStatusKey, vtsConnectionKey, vtsInventoryKey]),
] as const;

// The server sends every Twitch poll and prediction step, and every bot chat poll change, as a ChannelEvent.
// Each one lands in the history list (the journal row is written before the event), so the list and the
// running poll or prediction refetch.
const pollEventKeys = [
  { prefix: 'poll_', keys: [pollsKey, pollHistoryKey] },
  { prefix: 'prediction_', keys: [predictionsKey, pollHistoryKey] },
  { prefix: 'ChatPollChanged', keys: [pollHistoryKey] },
  { prefix: 'GiveawayChanged', keys: [giveawaysKey] },
  { prefix: 'media_share_', keys: [mediaShareQueueKey] },
] as const;

const pollEventsChanged = {
  event: 'ChannelEvent',
  handle: ({ queries, channelId }, event) => {
    const match = pollEventKeys.find((entry) => event.type.startsWith(entry.prefix));

    if (!match) return;

    invalidateKeys(queries, match.keys, channelId);
  },
} as const satisfies HubEventHandler<ChannelEventPayload>;

const permissionChanged = {
  event: 'PermissionChanged',
  handle: ({ queries, channelId }) => {
    const keys = [communityListsKey, viewerProfilesKey, communityFirstPageKey];

    invalidateKeys(queries, keys, channelId);
    void queries.invalidateQueries({ queryKey: currentUserQuery.queryKey });
  },
} as const satisfies HubEventHandler<PermissionChangedPayload>;

const configChanged = {
  event: 'ConfigChanged',
  handle: ({ queries, channelId }, change) => {
    const keys = configDomainKeys[change.domain] ?? channelPrefixes;

    invalidateKeys(queries, keys, channelId);
  },
} as const satisfies HubEventHandler<ConfigChangedPayload>;

export const liveHandlers = merge(eventHandlers, [
  redemptionRedeemed,
  redemptionStatusChanged,
  pollEventsChanged,
  permissionChanged,
  configChanged,
]);
