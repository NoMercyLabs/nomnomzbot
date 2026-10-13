// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

import { useOptimisticSave } from '@/composables/useOptimisticSave'

import { channelQuery, failed } from './channelQuery'
import { client } from './client'
import type { components } from './schema'

export type StreamInfo = components['schemas']['StreamInfoDto']
export type AdSchedule = components['schemas']['TwitchAdSchedule']

// What the form edits and sends: the same three fields as UpdateStreamRequest, never empty-as-null.
export type StreamInfoDraft = { title: string; gameName: string; tags: string[] }
export type Category = components['schemas']['CategoryDto']
export type PastStream = components['schemas']['StreamListItemDto']

const version = '1'
// The recent titles and categories come from the last streams.
const historySize = 20

// Twitch moves the next ad by this much per snooze.
export const SNOOZE_SECONDS: number = 300

export const streamInfoKey = (channelId: string | null): unknown[] => ['channels', channelId, 'stream']
export const adScheduleKey = (channelId: string | null): unknown[] => ['channels', channelId, 'ad-schedule']

export const streamHistoryKey = (channelId: string | null): unknown[] => ['channels', channelId, 'stream-history']
export const categorySearchKey = (channelId: string | null, query: string): unknown[] => [
  'channels',
  channelId,
  'stream-categories',
  query,
]

export function useStreamInfo(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<StreamInfo | null, Error> {
  return channelQuery(channelId, streamInfoKey, async (id: string): Promise<StreamInfo | null> => {
    const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/stream', {
      params: { path: { version, channelId: id } },
    })
    if (error !== undefined) throw failed('stream', response.status)
    return data?.data ?? null
  })
}

async function readAdSchedule(channelId: string): Promise<AdSchedule | null> {
  const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/live-ops/ads/schedule', {
    params: { path: { version, channelId } },
  })
  if (error !== undefined) throw failed('ad schedule', response.status)
  return data?.data ?? null
}

export function useAdSchedule(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<AdSchedule | null, Error> {
  return channelQuery(channelId, adScheduleKey, readAdSchedule)
}

// One snooze pushes the next ad back and uses up one of the snoozes Twitch still allows.
export function useSnoozeAd(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<AdSchedule | null, Error, void, number> {
  return useOptimisticSave<AdSchedule | null, void>({
    queryKey: () => adScheduleKey(toValue(channelId)),
    mutationFn: async (): Promise<AdSchedule | null> => {
      const id: string = toValue(channelId) as string
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/live-ops/ads/snooze', {
        params: { path: { version, channelId: id } },
      })
      if (error !== undefined) throw failed('snooze', response.status)
      return readAdSchedule(id)
    },
    apply: (current: AdSchedule | null | undefined): AdSchedule | null =>
      current
        ? {
            ...current,
            nextAdAt: Number(current.nextAdAt) + SNOOZE_SECONDS,
            snoozeCount: Math.max(0, Number(current.snoozeCount) - 1),
          }
        : null,
  })
}

// The newest streams first; the form takes its recent titles and categories from here.
export function useStreamHistory(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<PastStream[], Error> {
  return channelQuery(channelId, streamHistoryKey, async (id: string): Promise<PastStream[]> => {
    const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/analytics/streams', {
      params: { path: { version, channelId: id }, query: { PageSize: historySize } },
    })
    if (error !== undefined) throw failed('stream history', response.status)
    return [...(data?.data ?? [])]
  })
}

// Twitch does the searching; an empty search asks nothing.
export function useCategorySearch(
  channelId: MaybeRefOrGetter<string | null>,
  search: MaybeRefOrGetter<string>,
): UseQueryReturnType<Category[], Error> {
  return useQuery({
    queryKey: computed(() => categorySearchKey(toValue(channelId), toValue(search))),
    queryFn: async (): Promise<Category[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/stream/categories', {
        params: { path: { version, channelId: toValue(channelId) as string }, query: { query: toValue(search) } },
      })
      if (error !== undefined) throw failed('categories', response.status)
      return [...(data?.data ?? [])]
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(search).trim() !== ''),
  })
}

// The title, category and tags go to Twitch in one request; the cache shows them at once and rolls back on a refusal.
export function useSaveStreamInfo(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<StreamInfo | null, Error, StreamInfoDraft, number> {
  const queries: QueryClient = useQueryClient()
  return useOptimisticSave<StreamInfo | null, StreamInfoDraft>({
    queryKey: () => streamInfoKey(toValue(channelId)),
    mutationFn: async (draft: StreamInfoDraft): Promise<StreamInfo | null> => {
      const id: string = toValue(channelId) as string
      const { data, error, response } = await client.PUT('/api/v{version}/channels/{channelId}/stream', {
        params: { path: { version, channelId: id } },
        body: draft,
      })
      if (error !== undefined) throw failed('stream info', response.status)
      return data?.data ?? queries.getQueryData<StreamInfo | null>(streamInfoKey(id)) ?? null
    },
    apply: (current: StreamInfo | null | undefined, draft: StreamInfoDraft): StreamInfo | null =>
      current ? { ...current, ...draft } : null,
  })
}

// Starts a connect: the bot answers with the page of the platform where the streamer says yes.
export function useConnectIntegration(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<string, Error, { provider: string; scopeSetKey: string }, unknown> {
  return useMutation({
    mutationFn: async (variables: { provider: string; scopeSetKey: string }): Promise<string> => {
      const { data, error, response } = await client.POST('/api/v{version}/channels/{channelId}/integrations/{provider}/connect', {
        params: { path: { version, channelId: toValue(channelId) as string, provider: variables.provider } },
        body: { scopeSetKey: variables.scopeSetKey, returnUrl: window.location.href },
      })
      if (error !== undefined) throw failed('connect', response.status)
      const url: string | undefined = data?.data?.authorizeUrl
      if (!url) throw failed('connect', response.status)
      return url
    },
  })
}
