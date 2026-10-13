// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, useQuery, useQueryClient, type UseMutationReturnType, type UseQueryReturnType } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

import { client } from './client'

export const shieldKey = (channelId: string | null): unknown[] => ['channels', channelId, 'shield']

// The status reads live from the Twitch moderation API; the document types the answer as an open object.
async function fetchShield(channelId: string): Promise<boolean> {
  const { data } = await client.GET('/api/v{version}/channels/{channelId}/moderation/shield', {
    params: { path: { version: '1', channelId } },
  })
  const answer: unknown = data?.data
  return typeof answer === 'object' && answer !== null && (answer as { enabled?: unknown }).enabled === true
}

export function useShieldMode(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<boolean, Error> {
  return useQuery({
    queryKey: computed(() => shieldKey(toValue(channelId))),
    queryFn: () => fetchShield(toValue(channelId) as string),
    enabled: computed(() => toValue(channelId) !== null),
  })
}

export function useSetShieldMode(channelId: MaybeRefOrGetter<string | null>): UseMutationReturnType<void, Error, boolean, unknown> {
  const queries = useQueryClient()
  return useMutation({
    mutationFn: async (enabled: boolean): Promise<void> => {
      const { response } = await client.PATCH('/api/v{version}/channels/{channelId}/moderation/shield', {
        params: { path: { version: '1', channelId: toValue(channelId) as string } },
        body: { enabled },
      })
      if (!response.ok) throw new Error(`shield ${response.status}`)
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: shieldKey(toValue(channelId)) }),
  })
}
