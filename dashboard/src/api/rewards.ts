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

export type Reward = components['schemas']['RewardDetail']

const version = '1'
// Twitch allows at most 50 custom rewards per channel, so one page holds them all.
const pageSize = 100

export const rewardsKey = (channelId: string | null): unknown[] => ['channels', channelId, 'rewards']

export function useRewards(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<Reward[], Error> {
  return channelQuery(channelId, rewardsKey, async (id: string): Promise<Reward[]> => {
    const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/rewards', {
      params: { path: { version, channelId: id }, query: { PageSize: pageSize } },
    })
    if (error !== undefined) throw failed('rewards', response.status)
    return [...(data?.data ?? [])]
  })
}
