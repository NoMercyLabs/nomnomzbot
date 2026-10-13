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
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

import { useOptimisticSave } from '@/composables/useOptimisticSave'

import { client } from './client'
import type { components } from './schema'

export type CommunityMember = components['schemas']['UserDetailDto']
export type CommunityRow = components['schemas']['CommunityUserDto']
export type ViewerOption = components['schemas']['ViewerOptionDto']
export type TwitchViewer = components['schemas']['PlatformViewerDto']

const version = '1'

export const memberKey = (channelId: string | null, userId: string | null): unknown[] => [
  'community',
  channelId,
  'member',
  userId,
]

export const communityFirstPageKey = (channelId: string | null): unknown[] => [
  'community',
  channelId,
  'first-page',
]

// The id in the route is the Twitch user id (CommunityUserDto.id, UserDetailDto.id).
async function fetchMember(channelId: string, userId: string): Promise<CommunityMember | null> {
  const { data } = await client.GET('/api/v{version}/channels/{channelId}/community/{userId}', {
    params: { path: { version, channelId, userId } },
  })

  return data?.data ?? null
}

export function useCommunityMember(
  channelId: MaybeRefOrGetter<string | null>,
  userId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<CommunityMember | null, Error> {
  return useQuery({
    queryKey: computed(() => memberKey(toValue(channelId), toValue(userId))),
    queryFn: () => fetchMember(toValue(channelId)!, toValue(userId)!),
    enabled: computed(() => toValue(channelId) !== null && toValue(userId) !== null),
  })
}

// The first page of the channel's community, as the catalogue and any picker need it.
export function useCommunityFirstPage(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<CommunityRow[], Error> {
  return useQuery({
    queryKey: computed(() => communityFirstPageKey(toValue(channelId))),
    queryFn: async (): Promise<CommunityRow[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/community',
        {
          params: { path: { version, channelId: toValue(channelId)! }, query: { PageSize: 5 } },
        },
      )

      // A refusal is an error state, not an empty community.
      if (error !== undefined)
        throw new Error(`community ${response.status}`)

      return data?.data ?? []
    },
    enabled: computed(() => toValue(channelId) !== null),
  })
}

export type VipChange = { userId: string; vip: boolean }

// Grant or revoke VIP. That member's record changes at once; a failed save rolls it back and reaches the error inbox.
export function useVipChange(
  channelId: string,
): UseMutationReturnType<CommunityMember | null, Error, VipChange, number> {
  return useOptimisticSave<CommunityMember | null, VipChange>({
    queryKey: ({ userId }: VipChange) => memberKey(channelId, userId),
    mutationFn: async ({ userId, vip }: VipChange): Promise<CommunityMember | null> => {
      const params = { params: { path: { version, channelId, userId } } }
      const { error } = vip
        ? await client.POST('/api/v{version}/channels/{channelId}/community/{userId}/vip', params)
        : await client.DELETE('/api/v{version}/channels/{channelId}/community/{userId}/vip', params)

      if (error)
        throw new Error('vip change failed')

      return fetchMember(channelId, userId)
    },
    apply: (
      current: CommunityMember | null | undefined,
      { vip }: VipChange,
    ): CommunityMember | null =>
      current
        ? { ...current, isVip: vip }
        : null,
  })
}

export type BanInput = { userId: string; reason: string }

export function useBanMember(
  channelId: string,
): UseMutationReturnType<void, Error, BanInput, unknown> {
  const queries = useQueryClient()

  return useMutation({
    mutationFn: async ({ userId, reason }: BanInput): Promise<void> => {
      const { error } = await client.POST(
        '/api/v{version}/channels/{channelId}/community/{userId}/ban',
        {
          params: { path: { version, channelId, userId } },
          body: { reason },
        },
      )

      if (error)
        throw new Error('ban failed')
    },
    onSuccess: (_data: void, { userId }: BanInput) =>
      queries.invalidateQueries({ queryKey: memberKey(channelId, userId) }),
  })
}

const SEARCH_LIMIT = 25

export const viewerSearchKey = (channelId: string | null, q: string): unknown[] => [
  'community',
  channelId,
  'viewer-search',
  q,
]

// Every user the bot knows, in any channel; each option says whether the person is known in this channel.
export function useViewerSearch(
  channelId: MaybeRefOrGetter<string | null>,
  q: MaybeRefOrGetter<string>,
): UseQueryReturnType<ViewerOption[], Error> {
  return useQuery({
    queryKey: computed(() => viewerSearchKey(toValue(channelId), toValue(q))),
    queryFn: async (): Promise<ViewerOption[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/community/search',
        {
          params: {
            path: { version, channelId: toValue(channelId)! },
            query: { q: toValue(q), scope: 'all', limit: SEARCH_LIMIT },
          },
        },
      )

      if (error !== undefined)
        throw new Error(`viewer search ${response.status}`)

      return data?.data ?? []
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(q).trim() !== ''),
  })
}

export class TwitchUserNotFoundError extends Error {}

// A live Helix lookup by login. The bot saves the person it finds, so the id opens a profile at once.
export function useTwitchLookup(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<TwitchViewer, Error, string, unknown> {
  return useMutation({
    mutationFn: async (login: string): Promise<TwitchViewer> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/community/lookup',
        {
          params: { path: { version, channelId: toValue(channelId)! }, query: { login } },
        },
      )

      if (response.status === 404)
        throw new TwitchUserNotFoundError('twitch user not found')

      if (error !== undefined || !data?.data)
        throw new Error(`twitch lookup ${response.status}`)

      return data.data
    },
  })
}
