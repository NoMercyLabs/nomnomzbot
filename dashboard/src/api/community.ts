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

import { client } from './client'
import type { components } from './schema'

export type CommunityMember = components['schemas']['UserDetailDto']
export type CommunityRow = components['schemas']['CommunityUserDto']

const version = '1'
const VIP_PAGE_SIZE = 100

export const memberKey = (channelId: string | null, userId: string | null): unknown[] => ['community', channelId, 'member', userId]
export const vipKey = (channelId: string | null): unknown[] => ['community', channelId, 'vips']

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
export function useCommunityFirstPage(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<CommunityRow[], Error> {
  return useQuery({
    queryKey: computed(() => ['community', toValue(channelId), 'first-page']),
    queryFn: async (): Promise<CommunityRow[]> => {
      const { data } = await client.GET('/api/v{version}/channels/{channelId}/community', {
        params: { path: { version, channelId: toValue(channelId)! }, query: { PageSize: 5 } },
      })
      return data?.data ?? []
    },
    enabled: computed(() => toValue(channelId) !== null),
  })
}

// Twitch decides who is a VIP, and the member record does not carry it, so the VIP list answers (first 100).
async function fetchVipIds(channelId: string): Promise<string[]> {
  const { data } = await client.GET('/api/v{version}/channels/{channelId}/community', {
    params: { path: { version, channelId }, query: { role: 'vip', PageSize: VIP_PAGE_SIZE } },
  })
  return (data?.data ?? []).map((row: CommunityRow) => row.id)
}

export function useVipIds(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<string[], Error> {
  return useQuery({
    queryKey: computed(() => vipKey(toValue(channelId))),
    queryFn: () => fetchVipIds(toValue(channelId)!),
    enabled: computed(() => toValue(channelId) !== null),
  })
}

export type VipChange = { userId: string; vip: boolean }

// Grant or revoke VIP. The VIP list changes at once; a failed save rolls it back and reaches the error inbox.
export function useVipChange(channelId: string): UseMutationReturnType<string[], Error, VipChange, number> {
  return useOptimisticSave<string[], VipChange>({
    queryKey: vipKey(channelId),
    mutationFn: async ({ userId, vip }: VipChange): Promise<string[]> => {
      const params = { params: { path: { version, channelId, userId } } }
      const { error } = vip
        ? await client.POST('/api/v{version}/channels/{channelId}/community/{userId}/vip', params)
        : await client.DELETE('/api/v{version}/channels/{channelId}/community/{userId}/vip', params)
      if (error) throw new Error('vip change failed')
      return fetchVipIds(channelId)
    },
    apply: (current: string[] | undefined, { userId, vip }: VipChange): string[] => {
      const rest: string[] = (current ?? []).filter((id: string) => id !== userId)
      return vip ? [...rest, userId] : rest
    },
  })
}

export type BanInput = { userId: string; reason: string }

export function useBanMember(channelId: string): UseMutationReturnType<void, Error, BanInput, unknown> {
  const queries: QueryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ userId, reason }: BanInput): Promise<void> => {
      const { error } = await client.POST('/api/v{version}/channels/{channelId}/community/{userId}/ban', {
        params: { path: { version, channelId, userId } },
        body: { reason },
      })
      if (error) throw new Error('ban failed')
    },
    onSuccess: (_data: void, { userId }: BanInput) => queries.invalidateQueries({ queryKey: memberKey(channelId, userId) }),
  })
}
