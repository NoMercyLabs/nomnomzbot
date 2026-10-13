// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { type UseMutationReturnType, type UseQueryReturnType } from '@tanstack/vue-query'
import { toValue, type MaybeRefOrGetter } from 'vue'

import { useOptimisticSave } from '@/composables/useOptimisticSave'

import { channelQuery, failed } from './channelQuery'
import { client } from './client'
import type { components } from './schema'

export type HomeStats = components['schemas']['DashboardStatsDto']
export type ReviewQueueItem = components['schemas']['ModerationQueueItemDto']
export type IntegrationStatus = components['schemas']['IntegrationStatusDto']
export type BotStatus = components['schemas']['BotStatusDto']
export type ActivityEvent = components['schemas']['ActivityEventDto']

const version = '1'

// The realtime layer writes into these entries, so the keys are part of the module's surface.
export const homeStatsKey = (channelId: string | null): unknown[] => ['dashboard', channelId, 'stats']
export const reviewQueueKey = (channelId: string | null): unknown[] => ['channels', channelId, 'automod-queue']
export const connectionsKey = (channelId: string | null): unknown[] => ['channels', channelId, 'integrations-status']
export const botStatusKey = (channelId: string | null): unknown[] => ['channels', channelId, 'bot-status']
export const activityKey = (channelId: string | null): unknown[] => ['dashboard', channelId, 'activity']


export function useHomeStats(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<HomeStats | null, Error> {
  return channelQuery(channelId, homeStatsKey, async (id: string): Promise<HomeStats | null> => {
    const { data, error, response } = await client.GET('/api/v{version}/dashboard/{channelId}/stats', {
      params: { path: { version, channelId: id } },
    })
    if (error !== undefined) throw failed('stats', response.status)
    return data?.data ?? null
  })
}

export function useReviewQueue(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<ReviewQueueItem[], Error> {
  return channelQuery(channelId, reviewQueueKey, async (id: string): Promise<ReviewQueueItem[]> => {
    const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/moderation/automod/queue', {
      params: { path: { version, channelId: id }, query: { status: 'pending' } },
    })
    if (error !== undefined) throw failed('review queue', response.status)
    return data?.data ?? []
  })
}

export function useConnections(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<IntegrationStatus[], Error> {
  return channelQuery(channelId, connectionsKey, async (id: string): Promise<IntegrationStatus[]> => {
    const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/integrations/status', {
      params: { path: { version, channelId: id } },
    })
    if (error !== undefined) throw failed('connections', response.status)
    return [...(data?.data ?? [])]
  })
}

export function useBotStatus(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<BotStatus | null, Error> {
  return channelQuery(channelId, botStatusKey, async (id: string): Promise<BotStatus | null> => {
    const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/bot/status', {
      params: { path: { version, channelId: id } },
    })
    if (error !== undefined) throw failed('bot status', response.status)
    return data?.data ?? null
  })
}

export function useActivity(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<ActivityEvent[], Error> {
  return channelQuery(channelId, activityKey, async (id: string): Promise<ActivityEvent[]> => {
    const { data, error, response } = await client.GET('/api/v{version}/dashboard/{channelId}/activity', {
      params: { path: { version, channelId: id } },
    })
    if (error !== undefined) throw failed('activity', response.status)
    return data?.data ?? []
  })
}

export type QueueResolution = { queueItemId: string; action: 'approve' | 'deny' }

// A resolved message leaves the queue at once; a refused resolve puts it back and reaches the error inbox.
export function useResolveQueueItem(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<ReviewQueueItem[], Error, QueueResolution, number> {
  return useOptimisticSave<ReviewQueueItem[], QueueResolution>({
    queryKey: () => reviewQueueKey(toValue(channelId)),
    mutationFn: async ({ queueItemId, action }: QueueResolution): Promise<ReviewQueueItem[]> => {
      const id: string = toValue(channelId) as string
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/automod/queue/{queueItemId}/resolve',
        { params: { path: { version, channelId: id, queueItemId } }, body: { action } },
      )
      if (error !== undefined) throw failed('resolve', response.status)
      const { data } = await client.GET('/api/v{version}/channels/{channelId}/moderation/automod/queue', {
        params: { path: { version, channelId: id }, query: { status: 'pending' } },
      })
      return data?.data ?? []
    },
    apply: (current: ReviewQueueItem[] | undefined, { queueItemId }: QueueResolution): ReviewQueueItem[] =>
      (current ?? []).filter((item: ReviewQueueItem) => item.id !== queueItemId),
  })
}
