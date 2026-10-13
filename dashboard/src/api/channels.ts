// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { queryOptions, useMutation, useQuery, useQueryClient, type UseMutationReturnType, type UseQueryReturnType } from '@tanstack/vue-query'

import type { components } from './schema'
import { client } from './client'

export type ChannelSummary = components['schemas']['ChannelSummaryDto']
export type ModeratedChannel = components['schemas']['ModeratedChannelDto']

const version = { path: { version: '1' } }

// GET /channels declares ChannelDto in the OpenAPI document but returns ChannelSummaryDto rows
// (checked against the running bot, 2026-10-13), so the rows are read as the summary.
async function fetchMyChannels(): Promise<ChannelSummary[]> {
  const { data } = await client.GET('/api/v{version}/channels', { params: version })
  return (data?.data ?? []) as unknown as ChannelSummary[]
}

async function fetchModeratedChannels(): Promise<ModeratedChannel[]> {
  const { data } = await client.GET('/api/v{version}/channels/moderated', { params: version })
  return data?.data ?? []
}

export const myChannelsQuery = queryOptions({ queryKey: ['channels', 'mine'], queryFn: fetchMyChannels })
export const moderatedChannelsQuery = queryOptions({ queryKey: ['channels', 'moderated'], queryFn: fetchModeratedChannels })

export function useMyChannels(): UseQueryReturnType<ChannelSummary[], Error> {
  return useQuery(myChannelsQuery)
}

export function useModeratedChannels(): UseQueryReturnType<ModeratedChannel[], Error> {
  return useQuery(moderatedChannelsQuery)
}

// A channel the person moderates on Twitch, where the bot is not installed yet, opens in moderator mode.
export function useEnterModeratedChannel(): UseMutationReturnType<ChannelSummary | null, Error, string, unknown> {
  const queries = useQueryClient()
  return useMutation({
    mutationFn: async (twitchBroadcasterId: string): Promise<ChannelSummary | null> => {
      const { data } = await client.POST('/api/v{version}/channels/moderated/{twitchBroadcasterId}/enter', {
        params: { path: { version: '1', twitchBroadcasterId } },
      })
      return data?.data ?? null
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: ['channels'] }),
  })
}
