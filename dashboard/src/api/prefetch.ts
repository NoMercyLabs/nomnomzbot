// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { noop } from '@tanstack/vue-query';
import type { QueryClient, QueryExecuteOptions } from '@tanstack/vue-query';

import { lookup } from '@/lib/array';

import { ANALYTICS_RANGES, analyticsOverviewOptions, type AnalyticsRange } from './analytics';
import { chatEmotesOptions, chatHistoryOptions, userChatOptions } from './chat';
import { codeScriptsOptions } from './codeScripts';
import { builtinsOptions, commandsOptions } from './commands';
import { communityMemberOptions, personEmotesOptions, type CommunityMember } from './community';
import { discordConnectionsOptions } from './discord';
import {
  activityOptions,
  botStatusOptions,
  connectionsOptions,
  homeStatsOptions,
  reviewQueueOptions,
  supporterActivityOptions,
} from './home';
import { pollHistoryOptions } from './liveOps';
import {
  blockedTracksOptions,
  musicConfigOptions,
  musicHistoryOptions,
  musicQueueOptions,
  musicTopRequestersOptions,
} from './music';
import { obsBridgeOptions, obsConnectionOptions } from './obs';
import { communityListOptions, userNotesOptions, viewerProfileOptions } from './people';
import { pipelinesOptions } from './pipelines';
import { redemptionsOptions, rewardsOptions } from './rewards';
import { streamHistoryOptions, streamInfoOptions } from './stream';
import { supporterEventsOptions } from './supporters';
import { ttsConfigOptions } from './tts';
import { vtsConnectionOptions } from './vts';
import { requiredOverlaysOptions, widgetsOptions } from './widgets';

// A prefetched entry counts as fresh for this long, so the page that opens next does not ask again at once.
// An options object that sets its own staleTime (the chat history never goes stale) keeps it.
export const intentStaleMs = 30_000;

// The analytics page remembers its range under this key (AnalyticsView.vue); the warm reads the same range.
const analyticsRangeStorageKey = 'nnz.analytics.range';
const defaultAnalyticsRange: AnalyticsRange = 'r30';

type Warm = (queries: QueryClient, channelId: string) => Promise<void>;
type ChannelOptions<T> = (channelId: string, queries: QueryClient) => QueryExecuteOptions<T>;

// A warm that fails leaves the cache as it was: the page asks again when it opens.
async function warm<T>(queries: QueryClient, options: QueryExecuteOptions<T>): Promise<void> {
  await queries
    .query({
      staleTime: intentStaleMs,
      ...options,
    })
    .catch(noop);
}

// One warm for one channel read. The type parameter keeps each read's own data type.
function read<T>(options: ChannelOptions<T>): Warm {
  return (queries, channelId) => warm(queries, options(channelId, queries));
}

// One warm that runs every listed warm at once.
function reads(...warms: readonly Warm[]): Warm {
  return async (queries, channelId) => {
    const runs = warms.map((run) => run(queries, channelId));

    await Promise.all(runs);
  };
}

// The chat history reader merges into what the cache holds, so it takes the client too.
const chatHistory = read((channelId, queries) => chatHistoryOptions(queries, channelId));

const warmPeople: Warm = async (queries, channelId) => {
  const firstTab = communityListOptions(channelId, 'all', '');

  await queries
    .infiniteQuery({
      staleTime: intentStaleMs,
      ...firstTab,
    })
    .catch(noop);
};

const warmScripts: Warm = async (queries) => {
  await warm(queries, codeScriptsOptions());
};

function storedAnalyticsRange(): AnalyticsRange {
  const stored = localStorage.getItem(analyticsRangeStorageKey) ?? '';

  return stored in ANALYTICS_RANGES
    ? stored as AnalyticsRange
    : defaultAnalyticsRange;
}

const warmAnalytics: Warm = async (queries, channelId) => {
  const [summary, days, streams] = analyticsOverviewOptions(channelId, storedAnalyticsRange());

  await Promise.all([warm(queries, summary), warm(queries, days), warm(queries, streams)]);
};

// The queries each page runs when it opens, by route name. Every built page with data has an entry;
// the settings page and the "coming soon" pages read nothing on open, so the code chunk is all they need.
const routeWarmers = {
  'dashboard-home': reads(
    read(homeStatsOptions),
    read(reviewQueueOptions),
    read(connectionsOptions),
    read(botStatusOptions),
    read(activityOptions),
  ),
  'dashboard-chat': reads(chatHistory, read(chatEmotesOptions)),
  'dashboard-people': warmPeople,
  'dashboard-commands': reads(read(commandsOptions), read(builtinsOptions), read(pipelinesOptions)),
  'dashboard-music': reads(
    read(musicQueueOptions),
    read(musicConfigOptions),
    read(blockedTracksOptions),
    read(musicHistoryOptions),
    read(musicTopRequestersOptions),
    read(connectionsOptions),
  ),
  'dashboard-channel-points': reads(
    read(rewardsOptions),
    read(redemptionsOptions),
    read(pipelinesOptions),
  ),
  'dashboard-supporters': reads(
    read(supporterEventsOptions),
    read(supporterActivityOptions),
    read(homeStatsOptions),
  ),
  'dashboard-discord': reads(read(discordConnectionsOptions)),
  'dashboard-tts': reads(read(ttsConfigOptions)),
  'dashboard-overlays': reads(read(widgetsOptions), read(requiredOverlaysOptions)),
  'dashboard-obs': reads(read(obsConnectionOptions), read(obsBridgeOptions), read(widgetsOptions), read(requiredOverlaysOptions)),
  'dashboard-vtube-studio': reads(read(vtsConnectionOptions)),
  'dashboard-polls': reads(read(pollHistoryOptions)),
  'dashboard-pipelines': reads(read(pipelinesOptions)),
  'dashboard-scripts': warmScripts,
  'dashboard-stream': reads(
    read(streamInfoOptions),
    read(streamHistoryOptions),
    read(botStatusOptions),
    read(rewardsOptions),
  ),
  'dashboard-analytics': warmAnalytics,
} as const satisfies Record<string, Warm>;

// Fill the cache for the page a route name opens. Never throws: a failed prefetch is the page's own load later.
export async function prefetchRouteData(
  queries: QueryClient,
  routeName: string,
  channelId: string,
): Promise<void> {
  const run = lookup(routeWarmers, routeName);

  if (!run) return;

  await run(queries, channelId);
}

// The viewer profile and the Chat tab, read by the internal id the member answer holds.
async function warmByInternalId(
  queries: QueryClient,
  channelId: string,
  internalId: string,
): Promise<void> {
  const chat = queries
    .infiniteQuery({
      staleTime: intentStaleMs,
      ...userChatOptions(channelId, internalId),
    })
    .catch(noop);
  const profile = warm(queries, viewerProfileOptions(channelId, internalId));

  await Promise.all([profile, chat]);
}

// Fill the cache for the profile dialog of one person (the Twitch user id a name carries in data-user):
// the member, the notes, the Emotes tab, then what follows from the member's internal id.
export async function prefetchProfile(
  queries: QueryClient,
  channelId: string,
  userId: string,
): Promise<void> {
  const notes = warm(queries, userNotesOptions(channelId, userId));
  const emotes = warm(queries, personEmotesOptions(channelId, userId));
  const member = communityMemberOptions(channelId, userId);

  await warm(queries, member);

  const person = queries.getQueryData<CommunityMember | null>(member.queryKey);
  const internalId = person?.internalUserId;

  if (internalId) {
    await warmByInternalId(queries, channelId, internalId);
  }

  await Promise.all([notes, emotes]);
}
