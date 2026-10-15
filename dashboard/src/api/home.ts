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

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type HomeStats = components['schemas']['DashboardStatsDto'];
export type ReviewQueueItem = components['schemas']['ModerationQueueItemDto'];
export type IntegrationStatus = components['schemas']['IntegrationStatusDto'];
export type BotStatus = components['schemas']['BotStatusDto'];
export type ActivityEvent = components['schemas']['ActivityEventDto'];
export type ReplayActivityResult = components['schemas']['ReplayActivityResultDto'];

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

export function useHomeStats(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<HomeStats | null, Error> {
  return channelQuery(channelId, homeStatsKey, async (id: string): Promise<HomeStats | null> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/dashboard/{channelId}/stats',
      {
        params: { path: { version, channelId: id } },
      },
    );

    if (error !== undefined)
      throw failed('stats', response.status, error);

    return data?.data ?? null;
  }, statsRefreshMs);
}

export function useReviewQueue(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ReviewQueueItem[], Error> {
  return channelQuery(channelId, reviewQueueKey, async (id: string): Promise<ReviewQueueItem[]> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/moderation/automod/queue',
      {
        params: { path: { version, channelId: id }, query: { status: 'pending' } },
      },
    );

    if (error !== undefined)
      throw failed('review queue', response.status, error);

    return data?.data ?? [];
  });
}

export function useConnections(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<IntegrationStatus[], Error> {
  return channelQuery(
    channelId,
    connectionsKey,
    async (id: string): Promise<IntegrationStatus[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/integrations/status',
        {
          params: { path: { version, channelId: id } },
        },
      );

      if (error !== undefined)
        throw failed('connections', response.status, error);

      return [...(data?.data ?? [])];
    },
  );
}

export function useBotStatus(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<BotStatus | null, Error> {
  return channelQuery(channelId, botStatusKey, async (id: string): Promise<BotStatus | null> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/bot/status',
      {
        params: { path: { version, channelId: id } },
      },
    );

    if (error !== undefined)
      throw failed('bot status', response.status, error);

    return data?.data ?? null;
  });
}

export function useActivity(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ActivityEvent[], Error> {
  return channelQuery(channelId, activityKey, async (id: string): Promise<ActivityEvent[]> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/dashboard/{channelId}/activity',
      {
        params: { path: { version, channelId: id } },
      },
    );

    if (error !== undefined)
      throw failed('activity', response.status, error);

    return data?.data ?? [];
  });
}

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
      (current ?? []).filter((item: ReviewQueueItem) => item.id !== queueItemId),
  });
}
