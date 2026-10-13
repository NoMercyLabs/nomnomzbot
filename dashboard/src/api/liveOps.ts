// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { type UseQueryReturnType } from '@tanstack/vue-query'
import { type MaybeRefOrGetter } from 'vue'

import { channelQuery, failed } from './channelQuery'
import { client } from './client'
import type { components } from './schema'

export type Poll = components['schemas']['TwitchPoll']
export type Prediction = components['schemas']['TwitchPrediction']
export type Timer = components['schemas']['TimerListItem']

const version = '1'
const timerPageSize = 100

export const timersKey = (channelId: string | null): unknown[] => ['channels', channelId, 'timers']

export const pollsKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'live-ops-polls',
]
export const predictionsKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'live-ops-predictions',
]

// The channel's polls, active and ended. Use isRunning to pick the one that still takes votes.
export function usePolls(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Poll[], Error> {
  return channelQuery(channelId, pollsKey, async (id: string): Promise<Poll[]> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/live-ops/polls',
      {
        params: { path: { version, channelId: id } },
      },
    )
    if (error !== undefined) throw failed('polls', response.status)
    return [...(data?.data ?? [])]
  })
}

// The channel's predictions, open, locked and ended.
export function usePredictions(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Prediction[], Error> {
  return channelQuery(channelId, predictionsKey, async (id: string): Promise<Prediction[]> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/live-ops/predictions',
      {
        params: { path: { version, channelId: id } },
      },
    )
    if (error !== undefined) throw failed('predictions', response.status)
    return [...(data?.data ?? [])]
  })
}

// The channel's timers. Use isEnabled to pick the ones that fire.
export function useTimers(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Timer[], Error> {
  return channelQuery(channelId, timersKey, async (id: string): Promise<Timer[]> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/timers',
      {
        params: { path: { version, channelId: id }, query: { PageSize: timerPageSize } },
      },
    )
    if (error !== undefined) throw failed('timers', response.status)
    return [...(data?.data ?? [])]
  })
}
