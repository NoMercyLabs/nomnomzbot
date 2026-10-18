// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, type UseMutationReturnType, type UseQueryReturnType } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';

import { channelOptions, channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type HomeStats = components['schemas']['DashboardStats'];
export type ReviewQueueItem = components['schemas']['ModerationQueueItem'];
export type IntegrationStatus = components['schemas']['IntegrationStatus'];
export type BotStatus = components['schemas']['BotStatus'];
export type ActivityEvent = components['schemas']['ActivityEvent'];
export type ReplayActivityResult = components['schemas']['ReplayActivityResult'];

export class ActivityReplayError extends Error {
  constructor(public readonly status: number) {
    super(`activity replay failed (${status})`);
  }
}

const version = '1';

// The realtime layer writes into these entries, so the keys are part of the module's surface.
// Twitch sends no event for the viewer count, so the stats are read again each minute.
export const statsRefreshMs = 60_000;

export const homeStatsKey = (channelId: string | null): KeyParts => [
  'dashboard',
  channelId,
  'stats',
];

export const reviewQueueKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'automod-queue',
];

export const connectionsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'integrations-status',
];

export const botStatusKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'bot-status',
];

export const activityKey = (channelId: string | null): KeyParts => [
  'dashboard',
  channelId,
  'activity',
];

async function readHomeStats(id: string): Promise<HomeStats | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/dashboard/{channelId}/stats',
    {
      params: { path: { version, channelId: id } },
    },
  );

  if (error !== undefined)
    throw failed('stats', response.status, error);

  return data?.data ?? null;
}

async function readReviewQueue(id: string): Promise<ReviewQueueItem[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/automod/queue',
    {
      params: { path: { version, channelId: id }, query: { status: 'pending' } },
    },
  );

  if (error !== undefined)
    throw failed('review queue', response.status, error);

  return data?.data ?? [];
}

async function readConnections(id: string): Promise<IntegrationStatus[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/integrations/status',
    {
      params: { path: { version, channelId: id } },
    },
  );

  if (error !== undefined)
    throw failed('connections', response.status, error);

  return [...(data?.data ?? [])];
}

async function readBotStatus(id: string): Promise<BotStatus | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/bot/status',
    {
      params: { path: { version, channelId: id } },
    },
  );

  if (error !== undefined)
    throw failed('bot status', response.status, error);

  return data?.data ?? null;
}

async function readActivity(id: string): Promise<ActivityEvent[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/dashboard/{channelId}/activity',
    {
      params: { path: { version, channelId: id } },
    },
  );

  if (error !== undefined)
    throw failed('activity', response.status, error);

  return data?.data ?? [];
}

// The home page reads these five; the intent prefetch warms the same entries with the same readers.
export const homeStatsOptions = (id: string) => channelOptions(homeStatsKey, readHomeStats, id);

export const reviewQueueOptions = (id: string) =>
  channelOptions(reviewQueueKey, readReviewQueue, id);

export const connectionsOptions = (id: string) =>
  channelOptions(connectionsKey, readConnections, id);

export const botStatusOptions = (id: string) => channelOptions(botStatusKey, readBotStatus, id);
export const activityOptions = (id: string) => channelOptions(activityKey, readActivity, id);

export function useHomeStats(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<HomeStats | null, Error> {
  return channelQuery(channelId, homeStatsKey, readHomeStats, statsRefreshMs);
}

export function useReviewQueue(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ReviewQueueItem[], Error> {
  return channelQuery(channelId, reviewQueueKey, readReviewQueue);
}

export function useConnections(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<IntegrationStatus[], Error> {
  return channelQuery(channelId, connectionsKey, readConnections);
}

export function useBotStatus(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<BotStatus | null, Error> {
  return channelQuery(channelId, botStatusKey, readBotStatus);
}

export function useActivity(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ActivityEvent[], Error> {
  return channelQuery(channelId, activityKey, readActivity);
}

export const supporterActivityKey = (channelId: string | null): KeyParts => [
  ...activityKey(channelId),
  'supporters',
];

// Subs, gifts and cheers only, from a deeper window than the home feed, so a run of redemptions or shoutouts
// cannot push a gift off the Supporters page. It sits under activityKey, so the live refresh reaches it too.
const supporterActivityTypes = [
  'channel.subscribe',
  'channel.subscription.message',
  'channel.subscription.gift',
  'channel.cheer',
].join(',');

export function useSupporterActivity(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ActivityEvent[], Error> {
  return channelQuery(channelId, supporterActivityKey, readSupporterActivity);
}

async function readSupporterActivity(id: string): Promise<ActivityEvent[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/dashboard/{channelId}/activity',
    {
      params: {
        path: { version, channelId: id },
        query: { types: supporterActivityTypes },
      },
    },
  );

  if (error !== undefined)
    throw failed('activity', response.status, error);

  return data?.data ?? [];
}

export const supporterActivityOptions = (id: string) =>
  channelOptions(supporterActivityKey, readSupporterActivity, id);

export function useActivityReplay(
  channelId: MaybeRefOrGetter<string | null>,
) {
  return useMutation({
    mutationFn: async (eventId: string): Promise<ReplayActivityResult> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/dashboard/{channelId}/activity/{eventId}/replay',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          eventId,
        } } },
      );

      if (error !== undefined || !data?.data)
        throw new ActivityReplayError(response.status);

      return data.data;
    },
  });
}

export type QueueResolution = { queueItemId: string; action: 'approve' | 'deny' };

// A resolved message leaves the queue at once; a refused resolve puts it back and reaches the error inbox.
export function useResolveQueueItem(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<ReviewQueueItem[], Error, QueueResolution, number> {
  return useOptimisticSave<ReviewQueueItem[], QueueResolution>({
    describe: ({ action }) => ({ key: action === 'approve'
      ? 'approveReview'
      : 'denyReview' }),
    queryKey: () => reviewQueueKey(toValue(channelId)),
    mutationFn: async ({ queueItemId, action }: QueueResolution): Promise<ReviewQueueItem[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/automod/queue/{queueItemId}/resolve',
        {
          params: {
            path: {
              version,
              channelId: id,
              queueItemId,
            },
          },
          body: { action },
        },
      );

      if (error !== undefined)
        throw failed('resolve', response.status, error);

      const { data } = await client.GET(
        '/api/v{version}/channels/{channelId}/moderation/automod/queue',
        {
          params: { path: { version, channelId: id }, query: { status: 'pending' } },
        },
      );

      return data?.data ?? [];
    },
    apply: (
      current: ReviewQueueItem[] | undefined,
      { queueItemId }: QueueResolution,
    ): ReviewQueueItem[] =>
      (current ?? []).filter((item) => item.id !== queueItemId),
  });
}
