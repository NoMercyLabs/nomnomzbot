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

import type { QueryClient } from '@tanstack/vue-query';

import { currentUserQuery } from '@/api/auth';
import { myChannelsQuery } from '@/api/channels';
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
import { actionMatrixKey, membersKey, personGrantsKey } from '@/api/permissions';
import { rewardsKey } from '@/api/rewards';
import { adScheduleKey } from '@/api/stream';
import { vtsConnectionKey, vtsInventoryKey } from '@/api/vts';
import { outboundWebhooksKey } from '@/api/webhooks';
import { widgetsKey } from '@/api/widgets';
import { consentsKey, dataRequestsKey } from '@/api/yourData';
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
) {
  return {
    event,
    handle: ({ queries, channelId }) => {
      invalidateKeys(queries, keys, channelId);
    },
  } as const satisfies HubEventHandler<DashboardHubEvents[TEvent]>;
}

const myChannelsKey: QueryKeyBuilder = () => myChannelsQuery.queryKey;

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
  refreshing('VtsStatusChanged', [connectionsKey, botStatusKey, vtsConnectionKey, vtsInventoryKey]),
  // The channel list carries the suspension, so the frame enters or leaves the suspended view on its refetch.
  refreshing('ChannelSuspensionChanged', [myChannelsKey]),
] as const;

// The server sends every Twitch poll and prediction step, and every bot chat poll change, as a ChannelEvent.
// Each one lands in the history list (the journal row is written before the event), so the list and the
// running poll or prediction refetch.
const channelEventKeys = [
  { prefix: 'poll_', keys: [pollsKey, pollHistoryKey] },
  { prefix: 'prediction_', keys: [predictionsKey, pollHistoryKey] },
  { prefix: 'ChatPollChanged', keys: [pollHistoryKey] },
  { prefix: 'GiveawayChanged', keys: [giveawaysKey] },
  { prefix: 'media_share_', keys: [mediaShareQueueKey] },
  // The server sends every queue change as a ChannelEvent of this type, never as a hub method of its own.
  { prefix: 'sr_queue_changed', keys: musicKeys },
  // The director's one now-playing push: the card reads its state (paused by you, blocked) from the refetch.
  { prefix: 'music.nowPlaying', keys: musicKeys },
  { prefix: 'CustomDataSourceChanged', keys: [eventFeedsKey] },
  { prefix: 'webhook_', keys: [outboundWebhooksKey] },
  { prefix: 'DataRightsChanged', keys: [dataRequestsKey, consentsKey] },
  { prefix: 'FederationOptInChanged', keys: [federationOptInsKey] },
] as const;

const channelEventsChanged = {
  event: 'ChannelEvent',
  handle: ({ queries, channelId }, event) => {
    const match = channelEventKeys.find((entry) => event.type.startsWith(entry.prefix));

    if (!match) return;

    invalidateKeys(queries, match.keys, channelId);
  },
} as const satisfies HubEventHandler<ChannelEventPayload>;

const permissionChanged = {
  event: 'PermissionChanged',
  handle: ({ queries, channelId }) => {
    const keys = [
      communityListsKey,
      viewerProfilesKey,
      communityFirstPageKey,
      actionMatrixKey,
      membersKey,
      personGrantsKey,
    ];

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
  channelEventsChanged,
  permissionChanged,
  configChanged,
]);
